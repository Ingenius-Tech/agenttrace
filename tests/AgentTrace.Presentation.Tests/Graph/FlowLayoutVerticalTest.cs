using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Graph.GraphGeometry;
using static AgentTrace.Presentation.Tests.Graph.GraphTestKit;

namespace AgentTrace.Presentation.Tests.Graph;

/// <summary>The same story as horizontal, with time advancing downward.</summary>
public sealed class FlowLayoutVerticalTest
{
    private const GraphOrientation V = GraphOrientation.Vertical;

    private static readonly IReadOnlySet<int> NoFold = new HashSet<int>();

    /// <summary>Turn 0: prompt, one step, batch of 3 (one delegates to a subagent with 2 steps), a distant step and answer; turn 1: prompt and one step.</summary>
    private static FlowGraph BuildGraph()
    {
        var b = new FlowBuilder();
        b.Accept(new UserMessage(H(null, 0), "one"));
        b.Accept(Call(null, 1, "a", ToolKind.Read));
        b.Accept(new ToolResult(H(null, 2), "a", false, "ok"));
        b.Accept(Call(null, 3, "b1", ToolKind.Search));
        b.Accept(Call(null, 3, "b2", ToolKind.Search));
        b.Accept(Call(null, 3, "b3", ToolKind.Delegate));
        b.Accept(new ToolResult(H(null, 4), "b1", false, "ok"));
        b.Accept(new ToolResult(H(null, 4), "b2", true, "bad"));
        b.Accept(new ToolResult(H(null, 4), "b3", false, "x"));
        b.Accept(new SubagentStarted(H(null, 4), "b3", "sub", "research"));
        b.Accept(Call("sub", 5, "s1", ToolKind.Execute));
        b.Accept(new ToolResult(H("sub", 6), "s1", false, "ok"));
        b.Accept(Call("sub", 7, "s2", ToolKind.Execute));
        b.Accept(new ToolResult(H("sub", 8), "s2", false, "ok"));
        b.Accept(Call(null, 190, "c", ToolKind.Write));
        b.Accept(new ToolResult(H(null, 200), "c", false, "ok"));
        b.Accept(new AssistantText(H(null, 201), "done"));
        b.Accept(new TurnEnded(H(null, 202), null));
        b.Accept(new UserMessage(H(null, 300), "two"));
        b.Accept(Call(null, 301, "d", ToolKind.Execute));
        return b.Snapshot();
    }

    private static GraphGeometry Geometry() => FlowLayout.Of(BuildGraph(), NoFold, V);

    [Fact]
    public void TimeRunsDownAndParallelCallsSitSideBySide()
    {
        GraphGeometry g = Geometry();
        Assert.Equal(V, g.Orientation);
        Box b1 = g.FindStep("b1")!;
        Box b2 = g.FindStep("b2")!;
        Box b3 = g.FindStep("b3")!;
        Assert.Equal(b1.Y, b2.Y);
        Assert.Equal(b1.Y, b3.Y);
        Assert.True(b2.X >= b1.Right && b3.X >= b2.Right);
        Assert.True(g.FindStep("a")!.Y < b1.Y);
        Assert.True(g.FindStep("c")!.Y > b1.Y);
        Assert.True(g.FindTurnBox(1, Kind.Prompt)!.Y > g.FindTurnBox(0, Kind.Answer)!.Y);
    }

    [Fact]
    public void CardsAreWiderThanInHorizontalAndEveryBoxIsOnItsLane()
    {
        GraphGeometry g = Geometry();
        Assert.All(g.Boxes, b => Assert.True(b.W == VCardW && b.H == CardH));
        Box main = g.FindStep("a")!;
        Box sub = g.FindStep("s1")!;
        Assert.True(sub.X > main.Right, "the subagent goes to the right of the main one");
        Assert.True(sub.Y >= g.FindStep("b3")!.Y, "not before the call that launched it");
        Assert.Equal(2, g.Rows.Count);
        Assert.Equal<string>(["sub"], g.Rows[1].LaneIds);
    }

    [Fact]
    public void NoTwoBoxesOverlap()
    {
        IReadOnlyList<Box> boxes = Geometry().Boxes;
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Box a = boxes[i];
                Box b = boxes[j];
                Assert.False(a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom, $"{a} overlaps {b}");
            }
        }
    }

    [Fact]
    public void BoxesAreSortedByTimeThenByLane()
    {
        IReadOnlyList<Box> boxes = Geometry().Boxes;
        for (int i = 1; i < boxes.Count; i++)
        {
            Box p = boxes[i - 1];
            Box c = boxes[i];
            Assert.True(p.Y < c.Y || (p.Y == c.Y && p.X <= c.X));
        }
    }

    [Fact]
    public void HitFindsEveryBoxAndNothingInTheGaps()
    {
        GraphGeometry g = Geometry();
        foreach (Box b in g.Boxes)
        {
            Assert.Equal(b, g.Hit(b.CenterX, b.CenterY));
            Assert.Equal(b, g.Hit(b.X, b.Y));
            Assert.Equal(b, g.Hit(b.Right - 1, b.Bottom - 1));
        }
        Box any = g.Boxes[0];
        Assert.Null(g.Hit(any.CenterX, any.Bottom + 1));
        Assert.Null(g.Hit(-5, -5));
    }

    [Fact]
    public void VisibleReturnsExactlyTheBoxesTouchingTheRectangle()
    {
        GraphGeometry g = Geometry();
        var view = new Rect(0, g.Boxes[3].Y - 5, g.Width, RowStep * 3);
        List<Box> expected = g.Boxes.Where(b => b.Bottom > view.Y && b.Y < view.Bottom).ToList();
        Assert.Equal<Box>(expected, g.Visible(view));
        Assert.Equal(g.Boxes.Count, g.Visible(new Rect(0, 0, g.Width, g.Height)).Count);
        Assert.Empty(g.Visible(new Rect(0, 0, 1, 1)));
    }

    [Fact]
    public void EdgesLeaveFromTheBottomAndEnterThroughTheTop()
    {
        GraphGeometry g = Geometry();
        Box a = g.FindStep("a")!;
        Box b1 = g.FindStep("b1")!;
        Assert.Contains(g.Edges, e => e.X1 == a.CenterX && e.Y1 == a.Bottom && e.Y2 == b1.Y);
        Box parent = g.FindStep("b3")!;
        Box first = g.FindStep("s1")!;
        Assert.Contains(g.Edges, e => e.Dashed && e.Y1 == parent.Bottom && e.Y2 == first.Y);
    }

    [Fact]
    public void LongGapsAreMarkedAndBandsRunAlongY()
    {
        GraphGeometry g = Geometry();
        Assert.Equal("⋯ 3 min", g.Edges.Select(e => e.Label).FirstOrDefault(l => l is not null));
        Assert.Equal(2, g.Bands.Count);
        Assert.True(g.Bands[1].Start >= g.Bands[0].Start + g.Bands[0].Length);
        Assert.Equal(0, g.BandAt(g.Bands[0].Start + 1)!.TurnIndex);
        Assert.Equal(1, g.BandAt(g.Bands[1].Start + 1)!.TurnIndex);
        Assert.Equal(g.Boxes[^1].Bottom, g.ContentEnd);
    }

    [Fact]
    public void AFoldedTurnIsOneCardAndTheLaneDisappears()
    {
        GraphGeometry g = FlowLayout.Of(BuildGraph(), new HashSet<int> { 0 }, V);
        Assert.Equal(1, g.Boxes.Count(b => b.Kind == Kind.Folded));
        Assert.Single(g.Rows);
        Assert.Null(g.FindStep("s1"));
        Assert.Equal(RowStep, g.Bands[0].Length);
        Assert.True(g.Width < Geometry().Width);
    }

    [Fact]
    public void HorizontalLayoutIsUnchangedByTheRefactoring()
    {
        GraphGeometry h = FlowLayout.Of(BuildGraph());
        Assert.Equal(GraphOrientation.Horizontal, h.Orientation);
        Box a = h.FindStep("a")!;
        Assert.Equal(CardW, a.W);
        Assert.True(h.FindStep("b1")!.X > a.X);
        Assert.Equal(h.FindStep("b1")!.X, h.FindStep("b2")!.X);
    }

    [Fact]
    public void TheGeometryIsTheTransposedHistory()
    {
        GraphGeometry h = FlowLayout.Of(BuildGraph());
        GraphGeometry v = Geometry();
        Assert.Equal(h.Boxes.Count, v.Boxes.Count);
        Assert.Equal(h.Edges.Count, v.Edges.Count);
        Assert.Equal(h.Rows.Count, v.Rows.Count);
        Assert.Equal(h.Bands.Count, v.Bands.Count);
        foreach (string id in new[] { "a", "b1", "b2", "b3", "s1", "s2", "c", "d" })
        {
            Assert.Equal(h.FindStep(id)!.TurnIndex, v.FindStep(id)!.TurnIndex);
        }
    }
}
