using System.Diagnostics;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Core.Knowledge;

/// <summary>
/// Incrementally builds the <see cref="KnowledgeGraph"/> of a session: it is fed event by event and
/// <see cref="Snapshot"/> gives the graph up to that moment. Single thread (the one feeding it).
/// <para>
/// A turn starts with a user request (or after a <c>TurnEnded</c>) on the main thread; subagents
/// share the turn of the main one. Within a turn, each pair of related files adds a single point of weight.
/// </para>
/// </summary>
public sealed class KnowledgeBuilder
{
    /// <summary>Only files inside the project count; null = all.</summary>
    private readonly FilePath? _projectRoot;

    // Insertion order (LinkedHashMap in Java): fixes the order of nodes and edges of the graph.
    private readonly OrderedMap<FilePath, NodeState> _nodes = new();
    private readonly OrderedMap<EdgeKey, int> _edges = new();

    /// <summary>File (inside the project) of each call seen; null if it has none.</summary>
    private readonly Dictionary<string, FilePath?> _callTargets = [];
    private int _unplacedErrors;
    private bool _targetsKnown;

    private int _turns;
    private bool _turnOpen;

    /// <summary>What git reports as changed, with its attribution (see <see cref="SetChanges"/>).</summary>
    private IReadOnlyList<ChangeEntry> _gitChanges = [];

    /// <summary>What was read in this turn, in order.</summary>
    private readonly OrderedSet<FilePath> _turnReads = new();

    /// <summary>What the agent has changed in this turn, in order.</summary>
    private readonly OrderedSet<FilePath> _turnChanges = new();
    private readonly HashSet<FilePath> _turnTouched = [];

    /// <param name="projectRoot">files outside (the agent's temp folder, its memory…) and files or folders whose name starts with a dot (<c>.git</c>, <c>.claude</c>, <c>.gitignore</c>…) are not part of the project's knowledge; null = all</param>
    public KnowledgeBuilder(FilePath? projectRoot = null)
    {
        _projectRoot = projectRoot;
    }

    public void Accept(AgentEvent evt)
    {
        bool main = evt.Header.SubagentId == null;
        switch (evt)
        {
            case UserMessage:
                if (main) NewTurn();
                break;
            case TurnEnded:
                if (main) EndTurn();
                break;
            case FileRead e:
                if (NotFollowed(e.Path)) return;
                OpenTurn();
                NodeState node = Node(e.Path);
                node.Reads++;
                Touch(e.Path, node);
                _turnReads.Add(e.Path);
                break;
            case FileChanged e:
                OnChange(e);
                break;
            case ToolCall c:
                // Each call is remembered to attribute its error; a target outside the project is not part of this knowledge
                if (c.Target != null) _targetsKnown = true;
                _callTargets[c.CallId] = c.Target is { } t && !NotFollowed(t) ? t : null;
                break;
            case ToolResult { Error: true } r:
                OnError(r);
                break;
            case AssistantText or Thinking or SubagentStarted or ToolResult or TokenUsage or Skipped:
                break;
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>
    /// Replaces the changes git reports (all the observed repositories) with their attribution. They are state, not events: each call
    /// gives the whole current list. They cover what the trace cannot see, such as files written by a console command.
    /// </summary>
    public void SetChanges(IReadOnlyList<ChangeEntry> changes) => _gitChanges = changes;

    public KnowledgeGraph Snapshot()
    {
        List<KnowledgeGraph.Node> nodeList = _nodes.Entries.Select(e => e.Value.Freeze(e.Key)).ToList();
        int turns = AddGitChanges(nodeList);
        List<Edge> edgeList = _edges.Entries.Select(e => new Edge(e.Key.From, e.Key.To, e.Key.Relation, e.Value)).ToList();
        return new KnowledgeGraph(nodeList.ToValueList(), edgeList.ToValueList(), turns, _unplacedErrors, _targetsKnown);
    }

    /// <summary>
    /// Adds what git reports and the trace did not: a file whose change the trace already reported is left alone. The change counts as the
    /// agent's if the attribution points at calls, and as external otherwise (also if undated). It belongs to the last turn (the first if there
    /// is none) and creates no relations. Returns the number of turns of the graph.
    /// </summary>
    private int AddGitChanges(List<KnowledgeGraph.Node> nodes)
    {
        Dictionary<FilePath, int> index = [];
        for (int i = 0; i < nodes.Count; i++) index[nodes[i].Path] = i;
        int turns = _turns;
        foreach (ChangeEntry entry in _gitChanges)
        {
            FilePath path = entry.Change.Path;
            if (NotFollowed(path)) continue;
            bool known = index.TryGetValue(path, out int at);
            if (known && nodes[at].Changed) continue;
            turns = Math.Max(turns, 1);
            int turn = turns - 1;
            ChangeKind kind = entry.Change.Kind;
            bool byAgent = entry.Attribution.Confidence is Confidence.Exact or Confidence.Probable or Confidence.Ambiguous;
            KnowledgeGraph.Node current = known
                ? nodes[at]
                : new KnowledgeGraph.Node(path, null, 0, 0, 0, 0, 0, 1, turn, turn);
            KnowledgeGraph.Node changed = current with
            {
                LastChange = kind,
                Created = current.Created + (byAgent && kind == ChangeKind.Created ? 1 : 0),
                Modified = current.Modified + (byAgent && kind == ChangeKind.Modified ? 1 : 0),
                Deleted = current.Deleted + (byAgent && kind == ChangeKind.Deleted ? 1 : 0),
                External = current.External + (byAgent ? 0 : 1),
            };
            if (known) nodes[at] = changed;
            else
            {
                index[path] = nodes.Count;
                nodes.Add(changed);
            }
        }
        return turns;
    }

    private bool NotFollowed(FilePath path) => _projectRoot is { } root && (!path.IsUnder(root) || ProjectPaths.IsHidden(root, path));

    /// <summary>An error is attributed to the file of its call; without a file (or outside the project) it is counted apart. A result of an unseen call is ignored.</summary>
    private void OnError(ToolResult r)
    {
        if (!_callTargets.TryGetValue(r.CallId, out FilePath? target)) return;
        if (target is not { } path)
        {
            _unplacedErrors++;
            return;
        }
        OpenTurn();
        NodeState node = Node(path);
        node.Errors++;
        Touch(path, node);
    }

    private void OnChange(FileChanged e)
    {
        if (NotFollowed(e.Path)) return;
        OpenTurn();
        NodeState node = Node(e.Path);
        node.LastChange = e.Kind;
        Touch(e.Path, node);
        if (e.CallId == null)
        {
            node.External++; // not made by the agent: counts on the node, but creates no relations
            return;
        }
        switch (e.Kind)
        {
            case ChangeKind.Created:
                node.Created++;
                break;
            case ChangeKind.Modified:
                node.Modified++;
                break;
            case ChangeKind.Deleted:
                node.Deleted++;
                break;
            default:
                throw new UnreachableException();
        }
        if (!_turnChanges.Add(e.Path)) return; // already related to the rest in this turn
        foreach (FilePath read in _turnReads)
        {
            if (read != e.Path) Bump(new EdgeKey(read, e.Path, Relation.ReadBeforeEdit));
        }
        foreach (FilePath other in _turnChanges)
        {
            if (other == e.Path) continue;
            // Order by path text, like Java's String.compareTo (UTF-16 units, culture-free).
            bool otherFirst = string.CompareOrdinal(other.Value, e.Path.Value) < 0;
            Bump(otherFirst ? new EdgeKey(other, e.Path, Relation.CoChanged) : new EdgeKey(e.Path, other, Relation.CoChanged));
        }
    }

    private NodeState Node(FilePath path) => _nodes.GetOrAdd(path, _ => new NodeState());

    private void Touch(FilePath path, NodeState node)
    {
        if (_turnTouched.Add(path)) node.TurnsTouched++;
        if (node.FirstTurn < 0) node.FirstTurn = _turns - 1;
        node.LastTurn = _turns - 1;
    }

    private void Bump(EdgeKey key)
    {
        _edges[key] = _edges.TryGetValue(key, out int weight) ? weight + 1 : 1;
    }

    /// <summary>What happens without a prior request (a continuation) opens a turn.</summary>
    private void OpenTurn()
    {
        if (!_turnOpen) NewTurn();
    }

    private void NewTurn()
    {
        ResetTurn();
        _turns++;
        _turnOpen = true;
    }

    private void EndTurn()
    {
        ResetTurn();
        _turnOpen = false;
    }

    private void ResetTurn()
    {
        _turnReads.Clear();
        _turnChanges.Clear();
        _turnTouched.Clear();
    }

    private readonly record struct EdgeKey(FilePath From, FilePath To, Relation Relation);

    private sealed class NodeState
    {
        public ChangeKind? LastChange;
        public int Reads;
        public int Created;
        public int Modified;
        public int Deleted;
        public int External;
        public int TurnsTouched;
        public int FirstTurn = -1;
        public int LastTurn = -1;
        public int Errors;

        public KnowledgeGraph.Node Freeze(FilePath path) => new(path, LastChange, Reads, Created, Modified, Deleted, External, TurnsTouched, FirstTurn, LastTurn, Errors);
    }
}
