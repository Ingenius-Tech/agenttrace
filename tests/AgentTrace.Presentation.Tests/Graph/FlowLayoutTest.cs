using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Graph.GraphGeometry;
using static AgentTrace.Presentation.Tests.Graph.GraphTestKit;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class FlowLayoutTest
{
    /// <summary>Turn 1 with an answer, a batch of 3 and a subagent; turn 2 without a close.</summary>
    private static FlowGraph Sample()
    {
        List<AgentEvent> e =
        [
            new UserMessage(H(null, 0), "one"),
            Call(null, 1, "a", ToolKind.Read),
            new ToolResult(H(null, 2), "a", false, "ok"),
            Call(null, 3, "b1", ToolKind.Search),
            Call(null, 3, "b2", ToolKind.Search),
            Call(null, 3, "b3", ToolKind.Delegate),
            new ToolResult(H(null, 4), "b1", false, "ok"),
            new ToolResult(H(null, 4), "b2", true, "bad"),
            new ToolResult(H(null, 4), "b3", false, "launched"),
            new SubagentStarted(H(null, 4), "b3", "sub", "research"),
            new UserMessage(H("sub", 5), "task"),
            Call("sub", 6, "s1", ToolKind.Execute),
            new ToolResult(H("sub", 7), "s1", false, "ok"),
            Call("sub", 8, "s2", ToolKind.Execute),
            new ToolResult(H("sub", 9), "s2", false, "ok"),
            Call(null, 190, "c", ToolKind.Write),
            new ToolResult(H(null, 200), "c", false, "ok"),
            new AssistantText(H(null, 201), "done"),
            new TurnEnded(H(null, 202), TimeSpan.FromSeconds(202)),
            new UserMessage(H(null, 300), "two"),
            Call(null, 301, "d", ToolKind.Execute),
        ];
        return BuildFlow(e);
    }

    [Fact]
    public void EmptyGraphHasNoBoxes()
    {
        GraphGeometry g = FlowLayout.Of(new FlowBuilder().Snapshot());
        Assert.Empty(g.Boxes);
        Assert.Null(g.Hit(10, 10));
        Assert.Single(g.Rows);
    }

    [Fact]
    public void EveryStepPromptAndAnswerGetsABox()
    {
        FlowGraph graph = Sample();
        GraphGeometry g = FlowLayout.Of(graph);
        Assert.Equal(graph.StepCount, g.Boxes.Count(b => b.Kind == Kind.Step));
        Assert.Equal(2, g.Boxes.Count(b => b.Kind == Kind.Prompt));
        Assert.Equal(1, g.Boxes.Count(b => b.Kind == Kind.Answer));
        Assert.Equal(2, g.Bands.Count);
    }

    [Fact]
    public void ParallelCallsShareAColumnAndAreStackedWithoutOverlap()
    {
        GraphGeometry g = FlowLayout.Of(Sample());
        Box b1 = g.FindStep("b1")!;
        Box b2 = g.FindStep("b2")!;
        Box b3 = g.FindStep("b3")!;
        Assert.Equal(b1.X, b2.X);
        Assert.Equal(b1.X, b3.X);
        Assert.True(b2.Y >= b1.Bottom && b3.Y >= b2.Bottom);
    }

    [Fact]
    public void NoTwoBoxesOverlap()
    {
        IReadOnlyList<Box> boxes = FlowLayout.Of(Sample()).Boxes;
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Box a = boxes[i];
                Box b = boxes[j];
                bool overlap = a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
                Assert.False(overlap, $"{a} overlaps {b}");
            }
        }
    }

    [Fact]
    public void ColumnsGrowAlongALaneAndATurnComesAfterThePreviousOne()
    {
        FlowGraph graph = Sample();
        GraphGeometry g = FlowLayout.Of(graph);
        int last = -1;
        foreach (FlowGraph.Batch b in graph.BatchesOf("main"))
        {
            int x = g.FindStep(b.Steps[0].CallId)!.X;
            Assert.True(x > last, "x grows along a lane");
            last = x;
        }
        Box answer = g.FindTurnBox(0, Kind.Answer)!;
        Box prompt2 = g.FindTurnBox(1, Kind.Prompt)!;
        Assert.True(prompt2.X > answer.X);
        Assert.True(g.Bands[1].Start >= g.Bands[0].Start + g.Bands[0].Length);
    }

    [Fact]
    public void ASubagentStartsAtOrAfterTheCallThatLaunchedItAndLivesInItsOwnRow()
    {
        GraphGeometry g = FlowLayout.Of(Sample());
        Box parent = g.FindStep("b3")!;
        Box first = g.FindStep("s1")!;
        Assert.True(first.X >= parent.X);
        Assert.True(first.Y > parent.Y, "own lane below the main one");
        Assert.Equal(2, g.Rows.Count);
        Assert.Equal<string>(["sub"], g.Rows[1].LaneIds);
        Assert.Contains(g.Edges, e => e.Dashed && e.X1 == parent.Right && e.X2 == first.X);
    }

    [Fact]
    public void LanesThatDoNotCoincideInColumnsShareARow()
    {
        List<AgentEvent> e =
        [
            new UserMessage(H(null, 0), "p"),
            Call(null, 1, "m1", ToolKind.Delegate),
            new ToolResult(H(null, 2), "m1", false, "x"),
            new SubagentStarted(H(null, 2), "m1", "A", "a"),
            Call("A", 3, "a1", ToolKind.Read),
            new ToolResult(H("A", 4), "a1", false, "x"),
            Call(null, 10, "m2", ToolKind.Delegate),
            new ToolResult(H(null, 11), "m2", false, "x"),
            new SubagentStarted(H(null, 11), "m2", "B", "b"),
            Call("B", 12, "b1", ToolKind.Read),
        ];
        GraphGeometry g = FlowLayout.Of(BuildFlow(e));
        Assert.Equal(2, g.Rows.Count);
        Assert.Equal<string>(["A", "B"], g.Rows[1].LaneIds);
        Assert.Equal(g.FindStep("a1")!.Y, g.FindStep("b1")!.Y);
    }

    [Fact]
    public void LongGapsBetweenBatchesAreMarkedOnTheEdge()
    {
        GraphGeometry g = FlowLayout.Of(Sample());
        Assert.True(g.Edges.Any(e => e.Label is not null && e.Label.StartsWith("⋯ ", StringComparison.Ordinal)), string.Join(", ", g.Edges));
        Assert.Equal("⋯ 3 min", g.Edges.Select(x => x.Label).FirstOrDefault(l => l is not null));
    }

    [Fact]
    public void HitFindsEveryBoxByItsCenterAndNothingInTheGaps()
    {
        GraphGeometry g = FlowLayout.Of(Sample());
        foreach (Box b in g.Boxes)
        {
            Assert.Equal(b, g.Hit(b.X + b.W / 2.0, b.CenterY));
            Assert.Equal(b, g.Hit(b.X, b.Y));
        }
        Box any = g.Boxes[0];
        Assert.Null(g.Hit(any.Right + 1, any.CenterY));
        Assert.Null(g.Hit(-5, -5));
    }

    [Fact]
    public void VisibleReturnsExactlyTheBoxesTouchingTheRectangle()
    {
        GraphGeometry g = FlowLayout.Of(Sample());
        var view = new Rect(g.Boxes[2].X - 5, 0, ColW * 2, g.Height);
        List<Box> expected = g.Boxes.Where(b => b.Right > view.X && b.X < view.Right).ToList();
        Assert.Equal<Box>(expected, g.Visible(view));
        Assert.Empty(g.Visible(new Rect(0, 0, 1, 1)));
        Assert.Equal(g.Boxes.Count, g.Visible(new Rect(0, 0, g.Width, g.Height)).Count);
    }

    [Fact]
    public void LayoutIsDeterministic()
    {
        Assert.Equal<Box>(FlowLayout.Of(Sample()).Boxes, FlowLayout.Of(Sample()).Boxes);
        Assert.NotEqual(0, FlowLayout.Of(Sample()).Width);
        Assert.True(FlowLayout.Of(Sample()).Width >= CardW);
    }

    [Fact]
    public void SubagentTurnsBeforeAnyMainTurnStillGetBoxes()
    {
        List<AgentEvent> e = [Call("A", 1, "x", ToolKind.Read)];
        GraphGeometry g = FlowLayout.Of(BuildFlow(e));
        Assert.Single(g.Boxes);
    }
}
