using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Presentation.Knowledge;

/// <summary>
/// What is drawn of the graph: the user's filters and a node cap (the heaviest ones) so the drawing stays
/// readable and fast in huge sessions. No Avalonia.
/// <c>Hidden</c>: graph nodes that are not drawn (because of the filter or the cap).
/// </summary>
public sealed record KnowledgeView(ValueList<Node> Nodes, ValueList<Edge> Edges, int Hidden)
{
    public const int MaxNodes = 400;

    public static KnowledgeView Of(KnowledgeGraph graph, bool onlyChanged, int maxNodes) => Of(graph, onlyChanged, 1, maxNodes);

    /// <param name="graph">The whole graph.</param>
    /// <param name="onlyChanged">Only the files that have changed.</param>
    /// <param name="minWeight">Only relations that occurred in at least that many turns are drawn (removes the noise of what happened once).</param>
    /// <param name="maxNodes">Node cap: the heaviest ones stay.</param>
    public static KnowledgeView Of(KnowledgeGraph graph, bool onlyChanged, int minWeight, int maxNodes)
    {
        // Files with only errors (weight 0) exist in the graph for the heatmap, but are not drawn here
        List<Node> candidates = graph.Nodes.Where(n => n.Weight > 0 && (!onlyChanged || n.Changed)).ToList();
        var kept = new HashSet<FilePath>();
        // Descending weight and, at equal weight, the path as text (Java's String.compareTo: ordinal)
        foreach (Node n in candidates.OrderByDescending(n => n.Weight).ThenBy(n => n.Path.Value, StringComparer.Ordinal).Take(maxNodes))
        {
            kept.Add(n.Path);
        }
        // The graph order is kept: the layout does not change when weights change
        List<Node> nodes = graph.Nodes.Where(n => kept.Contains(n.Path)).ToList();
        List<Edge> edges = graph.Edges.Where(e => kept.Contains(e.From) && kept.Contains(e.To) && e.Weight >= minWeight).ToList();
        return new KnowledgeView(nodes.ToValueList(), edges.ToValueList(), graph.Nodes.Count(n => n.Weight > 0) - nodes.Count);
    }

    /// <summary>Radius a file is drawn with: grows with how much it was touched, up to a maximum.</summary>
    public static double Radius(Node node) => Math.Min(22, 7 + 3 * Math.Log(1 + node.Weight) / Math.Log(2));
}
