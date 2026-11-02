using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Tree;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Heatmap;

/// <summary>Heat trees for the tests, without touching the disk: the project is a fictitious path.</summary>
internal static class HeatFixture
{
    public static FilePath Root { get; } = new(Path.Combine(Path.GetTempPath(), "heat-root"));

    public static FilePath P(string relative) => Root.Combine(relative.Split('/'));

    public static ListedFile Listed(string relative, long size) => new(P(relative), size, false);

    public static ProjectListing Listing(params ListedFile[] files) => new(files.ToValueList(), false);

    public static KnowledgeGraph.Node Touched(string relative, int reads = 0, int created = 0, int modified = 0, int deleted = 0,
        int external = 0, int errors = 0, int turns = 1, int lastTurn = 0, ChangeKind? last = null) =>
        new(P(relative), last, reads, created, modified, deleted, external, turns, 0, lastTurn, errors);

    public static KnowledgeGraph Session(int turns, params KnowledgeGraph.Node[] nodes) =>
        new(nodes.ToValueList(), ValueList<KnowledgeGraph.Edge>.Empty, turns);

    public static HeatTree Build(ProjectListing listing, KnowledgeGraph graph, FileStatusIndex? index = null) =>
        HeatTree.Build(Root, listing, graph, index ?? FileStatusIndex.Empty(Root));

    /// <summary>A tree whose files are exactly the touched ones (all of 100 bytes).</summary>
    public static HeatTree TreeOf(params KnowledgeGraph.Node[] nodes) =>
        Build(Listing([.. nodes.Select(n => new ListedFile(n.Path, 100, false))]), Session(1, nodes));
}
