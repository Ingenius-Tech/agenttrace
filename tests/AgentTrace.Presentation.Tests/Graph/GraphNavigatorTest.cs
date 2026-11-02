using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Graph.GraphGeometry;
using static AgentTrace.Presentation.Graph.GraphNavigator.Move;
using static AgentTrace.Presentation.Tests.Graph.GraphTestKit;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class GraphNavigatorTest
{
    private static IReadOnlyList<AgentEvent> Events() =>
    [
        new UserMessage(H(null, 0), "p"),
        Call(null, 1, "a", ToolKind.Read), new ToolResult(H(null, 2), "a", false, "ok"),
        Call(null, 3, "b1", ToolKind.Search), Call(null, 3, "b2", ToolKind.Delegate),
        new ToolResult(H(null, 4), "b1", true, "bad"), new ToolResult(H(null, 4), "b2", false, "ok"),
        new SubagentStarted(H(null, 4), "b2", "sub", "x"),
        Call("sub", 5, "s1", ToolKind.Execute), new ToolResult(H("sub", 6), "s1", true, "bad"),
        Call(null, 7, "c", ToolKind.Write), new ToolResult(H(null, 8), "c", false, "ok"),
        new AssistantText(H(null, 9), "end"), new TurnEnded(H(null, 10), null),
    ];

    private static GraphGeometry Geometry() => FlowLayout.Of(BuildFlow(Events()));

    private static string Id(Box? b)
    {
        Box box = b!;
        return box.CallId ?? box.Kind.ToString().ToUpperInvariant();
    }

    [Fact]
    public void HorizontalMovesGoColumnByColumnAlongTheLane()
    {
        GraphGeometry g = Geometry();
        Box prompt = g.Boxes[0];
        Assert.Equal("a", Id(GraphNavigator.MoveFrom(g, prompt, Right)));
        Box a = g.FindStep("a")!;
        Assert.Equal("b1", Id(GraphNavigator.MoveFrom(g, a, Right)));
        Assert.Equal("PROMPT", Id(GraphNavigator.MoveFrom(g, a, Left)));
        Assert.Equal("c", Id(GraphNavigator.MoveFrom(g, g.FindStep("b2")!, Right)));
        Assert.Null(GraphNavigator.MoveFrom(g, g.Boxes[0], Left));
    }

    [Fact]
    public void VerticalMovesGoThroughParallelsThenToOtherLanes()
    {
        GraphGeometry g = Geometry();
        Assert.Equal("b2", Id(GraphNavigator.MoveFrom(g, g.FindStep("b1")!, Down)));
        Assert.Equal("s1", Id(GraphNavigator.MoveFrom(g, g.FindStep("b2")!, Down)));
        Assert.Equal("b2", Id(GraphNavigator.MoveFrom(g, g.FindStep("s1")!, Up)));
        Assert.Null(GraphNavigator.MoveFrom(g, g.FindStep("b1")!, Up));
    }

    [Fact]
    public void FirstLastAndErrors()
    {
        GraphGeometry g = Geometry();
        Assert.Equal("PROMPT", Id(GraphNavigator.MoveFrom(g, null, First)));
        Assert.Equal("ANSWER", Id(GraphNavigator.MoveFrom(g, g.FindStep("a")!, Last)));
        Assert.Equal("b1", Id(GraphNavigator.MoveFrom(g, null, NextError)));
        Assert.Equal("s1", Id(GraphNavigator.MoveFrom(g, g.FindStep("b1")!, NextError)));
        Assert.Equal("b1", Id(GraphNavigator.MoveFrom(g, g.FindStep("s1")!, NextError)));
    }

    [Fact]
    public void WithoutSelectionAnyMoveStartsAtTheFirstBox()
    {
        Assert.Equal("PROMPT", Id(GraphNavigator.MoveFrom(Geometry(), null, Right)));
    }

    [Fact]
    public void NoErrorsOrNoBoxesGiveNothing()
    {
        GraphGeometry empty = FlowLayout.Of(new FlowBuilder().Snapshot());
        Assert.Null(GraphNavigator.MoveFrom(empty, null, First));
        Assert.Null(GraphNavigator.MoveFrom(empty, null, NextError));
    }

    [Fact]
    public void SelectionOfABoxIsAStepOrATurn()
    {
        GraphGeometry g = Geometry();
        Assert.Equal(GraphSelection.ForStep("a"), GraphSelection.Of(g.FindStep("a")!));
        Assert.Equal(GraphSelection.ForTurn(0), GraphSelection.Of(g.Boxes[0]));
    }

    [Fact]
    public void ParsesSelectionArguments()
    {
        FlowGraph graph = BuildFlow(Events());
        Assert.Equal(GraphSelection.ForStep("b2"), GraphSelection.Parse("b2", graph));
        Assert.Equal(GraphSelection.ForTurn(0), GraphSelection.Parse(" turn:0 ", graph));
        Assert.Equal(GraphSelection.ForStep("c"), GraphSelection.Parse("last", graph));
        Assert.Equal(GraphSelection.ForStep("s1"), GraphSelection.Parse("last-error", graph));
        Assert.Null(GraphSelection.Parse("turn:7", graph));
        Assert.Null(GraphSelection.Parse("turn:x", graph));
        Assert.Null(GraphSelection.Parse("doesnotexist", graph));
        Assert.Null(GraphSelection.Parse("", graph));
        Assert.Null(GraphSelection.Parse(null, graph));
        Assert.Null(GraphSelection.Parse("last", new FlowBuilder().Snapshot()));
    }
}
