using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Presentation.Knowledge;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Knowledge.ForceLayout;

namespace AgentTrace.Presentation.Tests.Knowledge;

public sealed class KnowledgeLayoutTest
{
    private static readonly IReadOnlyDictionary<FilePath, Point> NoPositions = new Dictionary<FilePath, Point>();

    private static EventHeader H() => new(new SessionId("s"), "test", null, null, new SourceRef(new FilePath("t"), 1));

    // Fixed root: initial positions come from the hash of the path, so the test must not depend on the working directory
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\project" : "/project");

    private static FilePath P(string name) => Root.Combine(name);

    /// <summary>Two groups that change together (A: a0..a5, B: b0..b5) and a read-only file that joins both.</summary>
    private static KnowledgeGraph TwoClusters()
    {
        var b = new KnowledgeBuilder();
        foreach (string group in new[] { "a", "b" })
        {
            for (int turn = 0; turn < 2; turn++)
            {
                b.Accept(new UserMessage(H(), "p"));
                for (int i = 0; i < 6; i++) b.Accept(new FileChanged(H(), P(group + i), ChangeKind.Modified, "w"));
            }
        }
        return b.Snapshot();
    }

    private static double Dist(Point a, Point b) => double.Hypot(a.X - b.X, a.Y - b.Y);

    [Fact]
    public void ViewKeepsTheHeaviestNodesAndOnlyTheEdgesBetweenThem()
    {
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(H(), "p"));
        for (int i = 0; i < 5; i++) b.Accept(new FileRead(H(), P("r" + i), "r"));
        b.Accept(new FileChanged(H(), P("c0"), ChangeKind.Modified, "w"));
        b.Accept(new FileChanged(H(), P("c0"), ChangeKind.Modified, "w"));
        b.Accept(new FileChanged(H(), P("c1"), ChangeKind.Created, "w"));
        KnowledgeGraph g = b.Snapshot();
        KnowledgeView all = KnowledgeView.Of(g, false, 100);
        Assert.Equal(7, all.Nodes.Count);
        Assert.Equal(0, all.Hidden);
        KnowledgeView changed = KnowledgeView.Of(g, true, 100);
        Assert.Equal(2, changed.Nodes.Count);
        Assert.Equal(5, changed.Hidden);
        Assert.All(changed.Edges, e => Assert.True(e.From.ToString().Contains('c') && e.To.ToString().Contains('c')));
        KnowledgeView top = KnowledgeView.Of(g, false, 2);
        Assert.Equal(2, top.Nodes.Count);
        Assert.Contains(top.Nodes, n => n.Path == P("c0"));
        Assert.Equal(5, top.Hidden);
        Assert.Equal<KnowledgeGraph.Node>(g.Nodes.Where(n => top.Nodes.Contains(n)).ToList(), top.Nodes);
    }

    [Fact]
    public void MinimumWeightKeepsOnlyRepeatedRelationsButAllTheNodes()
    {
        KnowledgeGraph g = TwoClusters();
        var extra = new KnowledgeBuilder();
        extra.Accept(new UserMessage(H(), "p"));
        extra.Accept(new FileChanged(H(), P("a0"), ChangeKind.Modified, "w"));
        extra.Accept(new FileChanged(H(), P("z"), ChangeKind.Modified, "w"));
        KnowledgeView once = KnowledgeView.Of(extra.Snapshot(), false, 1, 100);
        KnowledgeView repeated = KnowledgeView.Of(extra.Snapshot(), false, 2, 100);
        Assert.Single(once.Edges);
        Assert.Empty(repeated.Edges);
        Assert.Equal(2, repeated.Nodes.Count);
        Assert.Equal(g.Edges.Count, KnowledgeView.Of(g, false, 2, 100).Edges.Count);
    }

    [Fact]
    public void RadiusGrowsWithWeightUpToAMaximum()
    {
        KnowledgeGraph g = TwoClusters();
        double small = KnowledgeView.Radius(g.Nodes[0]);
        Assert.True(small >= 7 && small <= 22);
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(H(), "p"));
        for (int i = 0; i < 5000; i++) b.Accept(new FileRead(H(), P("x"), "r"));
        Assert.Equal(22, KnowledgeView.Radius(b.Snapshot().Nodes[0]));
    }

    [Fact]
    public void LayoutIsDeterministicAndFinite()
    {
        KnowledgeView view = KnowledgeView.Of(TwoClusters(), false, 400);
        Dictionary<FilePath, Point> first = Run(view, NoPositions, 150);
        Dictionary<FilePath, Point> second = Run(view, NoPositions, 150);
        Assert.Equal(first, second);
        Assert.Equal(12, first.Count);
        foreach (Point pt in first.Values) Assert.True(double.IsFinite(pt.X) && double.IsFinite(pt.Y));
    }

    [Fact]
    public void RelatedFilesEndUpCloserThanUnrelatedOnes()
    {
        KnowledgeView view = KnowledgeView.Of(TwoClusters(), false, 400);
        Dictionary<FilePath, Point> pos = Run(view, NoPositions, 200);
        double intra = 0;
        int intraN = 0;
        double inter = 0;
        int interN = 0;
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 6; j++)
            {
                if (i < j)
                {
                    intra += Dist(pos[P("a" + i)], pos[P("a" + j)]) + Dist(pos[P("b" + i)], pos[P("b" + j)]);
                    intraN += 2;
                }
                inter += Dist(pos[P("a" + i)], pos[P("b" + j)]);
                interN++;
            }
        }
        Assert.True(intra / intraN < inter / interN, $"intra {intra / intraN} inter {inter / interN}");
    }

    [Fact]
    public void NoTwoNodesSitOnTopOfEachOther()
    {
        KnowledgeView view = KnowledgeView.Of(TwoClusters(), false, 400);
        Dictionary<FilePath, Point> pos = Run(view, NoPositions, 200);
        foreach (Point a in pos.Values)
        {
            foreach (Point b in pos.Values)
            {
                if (!ReferenceEquals(a, b)) Assert.True(Dist(a, b) > 8, $"nodos casi encima: {Dist(a, b)}");
            }
        }
    }

    [Fact]
    public void AnUpdateBarelyMovesWhatWasAlreadyPlaced()
    {
        KnowledgeView view = KnowledgeView.Of(TwoClusters(), false, 400);
        Dictionary<FilePath, Point> settled = Run(view, NoPositions, 200);
        Dictionary<FilePath, Point> again = Run(view, settled, 25);
        double max = 0;
        foreach (FilePath path in settled.Keys) max = Math.Max(max, Dist(settled[path], again[path]));
        Assert.True(max < 15, $"maximum movement {max}");
    }

    [Fact]
    public void ANewFileAppearsNextToItsNeighbourAndTheRestStaysPut()
    {
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(H(), "p"));
        for (int i = 0; i < 6; i++) b.Accept(new FileChanged(H(), P("a" + i), ChangeKind.Modified, "w"));
        Dictionary<FilePath, Point> before = Run(KnowledgeView.Of(b.Snapshot(), false, 400), NoPositions, 150);
        b.Accept(new FileChanged(H(), P("new"), ChangeKind.Created, "w"));
        Dictionary<FilePath, Point> after = Run(KnowledgeView.Of(b.Snapshot(), false, 400), before, 25);
        Assert.Equal(7, after.Count);
        double moved = 0;
        foreach (FilePath path in before.Keys) moved = Math.Max(moved, Dist(before[path], after[path]));
        Assert.True(moved < 60, $"what was already placed moved {moved}");
        Assert.True(Dist(after[P("new")], after[P("a0")]) < 400);
    }

    [Fact]
    public void EmptyAndSingleNodeViewsWork()
    {
        Assert.Empty(Run(KnowledgeView.Of(new KnowledgeBuilder().Snapshot(), false, 10), NoPositions, 50));
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(H(), "p"));
        b.Accept(new FileRead(H(), P("only"), "r"));
        Dictionary<FilePath, Point> one = Run(KnowledgeView.Of(b.Snapshot(), false, 10), NoPositions, 50);
        Assert.Single(one);
        Assert.False(double.IsNaN(one[P("only")].X));
    }

    [Fact]
    public void PanAndZoomKeepTheAnchorFixedAndRespectTheLimits()
    {
        var z = new PanZoom();
        z.PanBy(30, -20);
        double wx = z.ToWorldX(400);
        double wy = z.ToWorldY(300);
        z.ZoomAt(1.5, 400, 300);
        Assert.Equal(1.5, z.Scale, 1e-9);
        Assert.Equal(wx, z.ToWorldX(400), 1e-9);
        Assert.Equal(wy, z.ToWorldY(300), 1e-9);
        z.ZoomAt(1000, 0, 0);
        Assert.Equal(PanZoom.MaxScale, z.Scale);
        z.ZoomAt(1e-9, 0, 0);
        Assert.Equal(PanZoom.MinScale, z.Scale);
        Assert.Equal(12.5, z.ToWorldX(z.ToScreenX(12.5)), 1e-9);
    }

    [Fact]
    public void FitFramesTheWholeGraphAndCenterOnMovesTheView()
    {
        var z = new PanZoom();
        z.Fit(-500, -250, 500, 250, 800, 600, 40);
        Assert.True(z.ToScreenX(-500) >= 40 - 1e-9 && z.ToScreenX(500) <= 760 + 1e-9);
        Assert.True(z.ToScreenY(-250) >= 40 - 1e-9 && z.ToScreenY(250) <= 560 + 1e-9);
        Assert.Equal(400, z.ToScreenX(0), 1e-9);
        z.CenterOn(100, 50, 800, 600);
        Assert.Equal(400, z.ToScreenX(100), 1e-9);
        Assert.Equal(300, z.ToScreenY(50), 1e-9);
    }
}
