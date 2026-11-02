using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Knowledge;

/// <summary>
/// Which files the agent has touched and how they relate to each other, according to the trace. Immutable; obtained from a
/// <see cref="KnowledgeBuilder"/>. It depends on no agent and no Avalonia. <c>Turns</c>: turns of the session seen so far.
/// <c>UnplacedErrors</c>: failed calls that cannot be attributed to a file of the project.
/// <c>TargetsKnown</c>: some call reported its file (<c>ToolCall.Target</c>); otherwise the agent does not allow attributing errors.
/// </summary>
public sealed record KnowledgeGraph(ValueList<KnowledgeGraph.Node> Nodes, ValueList<KnowledgeGraph.Edge> Edges, int Turns,
    int UnplacedErrors = 0, bool TargetsKnown = false)
{
    /// <summary>
    /// A file. <c>LastChange</c>: last change (agent or external), or null if the file has only been read.
    /// <c>Reads</c>: times the agent read it. <c>External</c>: changes that no call of the agent explains.
    /// <c>TurnsTouched</c>: distinct turns in which it was read or changed. <c>FirstTurn</c>/<c>LastTurn</c>: index (from 0) of the first
    /// and the last turn in which it was touched. <c>Errors</c>: calls on the file that ended in error (they do not count in <see cref="Weight"/>).
    /// </summary>
    public sealed record Node(FilePath Path, ChangeKind? LastChange, int Reads, int Created, int Modified, int Deleted, int External, int TurnsTouched, int FirstTurn, int LastTurn, int Errors = 0)
    {
        /// <summary>Changes made by the agent.</summary>
        public int AgentChanges => Created + Modified + Deleted;

        public bool Changed => AgentChanges + External > 0;

        /// <summary>Importance of the file: how much it has been touched.</summary>
        public int Weight => Reads + AgentChanges + External;

        /// <summary>The changes were made only by someone other than the agent (the user or another process).</summary>
        public bool OnlyExternal => External > 0 && AgentChanges == 0;
    }

    public enum Relation
    {
        /// <summary><c>From</c> was read before the agent changed <c>To</c>, in the same turn.</summary>
        ReadBeforeEdit,

        /// <summary>The agent changed both in the same turn (undirected: <c>From</c> precedes <c>To</c> by path).</summary>
        CoChanged,
    }

    /// <summary><c>Weight</c>: turns in which the relation occurred.</summary>
    public sealed record Edge(FilePath From, FilePath To, Relation Relation, int Weight);

    /// <summary>The node of that file, or null (<c>node(path)</c> in Java).</summary>
    public Node? FindNode(FilePath path) => Nodes.FirstOrDefault(n => n.Path == path);

    /// <summary>The relations of a file, with their other end, from most to least weight (equal weight: graph order).</summary>
    public IReadOnlyList<Edge> EdgesOf(FilePath path) =>
        Edges.Where(e => e.From == path || e.To == path).OrderByDescending(e => e.Weight).ToList();
}
