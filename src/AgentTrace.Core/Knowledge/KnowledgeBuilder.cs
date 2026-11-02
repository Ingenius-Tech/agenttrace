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

    public KnowledgeGraph Snapshot()
    {
        List<KnowledgeGraph.Node> nodeList = _nodes.Entries.Select(e => e.Value.Freeze(e.Key)).ToList();
        List<Edge> edgeList = _edges.Entries.Select(e => new Edge(e.Key.From, e.Key.To, e.Key.Relation, e.Value)).ToList();
        return new KnowledgeGraph(nodeList.ToValueList(), edgeList.ToValueList(), _turns, _unplacedErrors, _targetsKnown);
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
