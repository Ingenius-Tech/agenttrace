using System.Diagnostics;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Graph.GraphGeometry;
using static AgentTrace.Presentation.Tests.Graph.GraphTestKit;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class FlowLayoutFoldingTest
{
    private static HashSet<int> Folded(params int[] turns) => [.. turns];

    /// <summary>Turn 0: prompt, 5 parallel calls and a subagent with one step; turn 1: prompt and one step.</summary>
    private static FlowGraph BuildGraph()
    {
        var b = new FlowBuilder();
        b.Accept(new UserMessage(H(null, 0), "one"));
        for (int i = 0; i < 5; i++) b.Accept(new ToolCall(H(null, 1), "p" + i, "Read", ToolKind.Read, "f" + i, "{}"));
        for (int i = 0; i < 5; i++) b.Accept(new ToolResult(H(null, 2), "p" + i, false, "ok"));
        b.Accept(new ToolCall(H(null, 3), "d", "Agent", ToolKind.Delegate, "sub", "{}"));
        b.Accept(new ToolResult(H(null, 4), "d", false, "x"));
        b.Accept(new SubagentStarted(H(null, 4), "d", "S", "task"));
        b.Accept(new ToolCall(H("S", 5), "s1", "Bash", ToolKind.Execute, "ls", "{}"));
        b.Accept(new AssistantText(H(null, 6), "done"));
        b.Accept(new TurnEnded(H(null, 7), null));
        b.Accept(new UserMessage(H(null, 10), "two"));
        b.Accept(new ToolCall(H(null, 11), "u", "Bash", ToolKind.Execute, "pwd", "{}"));
        return b.Snapshot();
    }

    [Fact]
    public void AFoldedTurnIsOneSummaryColumnWithoutItsSteps()
    {
        GraphGeometry g = FlowLayout.Of(BuildGraph(), Folded(0));
        List<Box> folded = g.Boxes.Where(b => b.Kind == Kind.Folded).ToList();
        Assert.Single(folded);
        Assert.Equal(0, folded[0].TurnIndex);
        Assert.True(g.FindStep("p0") is null && g.FindStep("s1") is null && g.FindStep("d") is null, "the steps of the folded turn are not laid out");
        Assert.True(g.FindTurnBox(0, Kind.Prompt) is null && g.FindTurnBox(0, Kind.Answer) is null);
        Assert.NotNull(g.FindStep("u"));
        Assert.Equal(2, g.Bands.Count);
        Assert.Equal(ColW, g.Bands[0].Length);
    }

    [Fact]
    public void FoldingShrinksTheRowsAndDropsTheSubagentLane()
    {
        GraphGeometry open = FlowLayout.Of(BuildGraph(), Folded());
        GraphGeometry folded = FlowLayout.Of(BuildGraph(), Folded(0));
        Assert.Equal(2, open.Rows.Count);
        Assert.Single(folded.Rows);
        Assert.True(folded.Height < open.Height);
        Assert.True(folded.Width < open.Width);
        Assert.DoesNotContain(folded.Edges, e => e.Dashed);
    }

    [Fact]
    public void FoldedBoxesAreHitAndSelectAsTheirTurn()
    {
        GraphGeometry g = FlowLayout.Of(BuildGraph(), Folded(0));
        Box box = g.Boxes.First(b => b.Kind == Kind.Folded);
        Assert.Equal(box, g.Hit(box.X + 5, box.Y + 5));
        Assert.Equal(GraphSelection.ForTurn(0), GraphSelection.Of(box));
    }

    [Fact]
    public void BandAtFindsTheTurnUnderAnX()
    {
        GraphGeometry g = FlowLayout.Of(BuildGraph(), Folded());
        Assert.Equal(0, g.BandAt(g.Bands[0].Start + 1)!.TurnIndex);
        Assert.Equal(1, g.BandAt(g.Bands[1].Start + 1)!.TurnIndex);
        Assert.Null(g.BandAt(-100));
        Assert.Null(g.BandAt(g.Width + 1000));
    }

    [Fact]
    public void NoCollapsedTurnsGivesTheSameLayoutAsBefore()
    {
        Assert.Equal<Box>(FlowLayout.Of(BuildGraph()).Boxes, FlowLayout.Of(BuildGraph(), Folded()).Boxes);
        Assert.DoesNotContain(FlowLayout.Of(BuildGraph()).Boxes, b => b.Kind == Kind.Folded);
    }

    [Fact]
    public void FiveThousandStepsAreLaidOutFastWhenMostTurnsAreFolded()
    {
        var b = new FlowBuilder();
        int second = 0;
        for (int t = 0; t < 100; t++)
        {
            b.Accept(new UserMessage(H(null, second++), "turn " + t));
            for (int i = 0; i < 50; i++)
            {
                string id = "t" + t + "s" + i;
                b.Accept(new ToolCall(H(null, second++), id, "Bash", ToolKind.Execute, "x", "{}"));
                b.Accept(new ToolResult(H(null, second++), id, false, "ok"));
            }
            b.Accept(new TurnEnded(H(null, second++), null));
        }
        FlowGraph graph = b.Snapshot();
        Assert.Equal(5_000, graph.StepCount);
        IReadOnlySet<int> collapsed = new TurnFolding().Collapsed(graph, null);
        Assert.Equal(99, collapsed.Count);
        FlowLayout.Of(graph, collapsed); // warm-up
        long start = Stopwatch.GetTimestamp();
        GraphGeometry folded = FlowLayout.Of(graph, collapsed);
        long foldedMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        GraphGeometry all = FlowLayout.Of(graph);
        long allMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Console.WriteLine($"FlowLayoutFoldingTest: 5000 steps, folded {foldedMs} ms ({folded.Boxes.Count} boxes), unfolded {allMs} ms ({all.Boxes.Count} boxes)");
        Assert.True(foldedMs < 50, $"folded: {foldedMs} ms");
        Assert.True(folded.Boxes.Count < 200);
    }
}
