using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Tests.Graph.GraphTestKit;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class TurnFoldingTest
{
    /// <summary>Four turns with one step each: the steps are c0..c3.</summary>
    private static FlowGraph BuildGraph(int turns)
    {
        var b = new FlowBuilder();
        for (int i = 0; i < turns; i++)
        {
            b.Accept(new UserMessage(H(null, i * 10), "p" + i));
            b.Accept(new ToolCall(H(null, i * 10 + 1), "c" + i, "Bash", ToolKind.Execute, "x", "{}"));
            b.Accept(new ToolResult(H(null, i * 10 + 2), "c" + i, false, "ok"));
        }
        return b.Snapshot();
    }

    private static void AssertSet(IReadOnlySet<int> actual, params int[] expected) =>
        Assert.True(actual.SetEquals(expected), "expected {" + string.Join(", ", expected) + "}, got {" + string.Join(", ", actual) + "}");

    [Fact]
    public void ByDefaultEverythingButTheLastTurnIsFolded()
    {
        AssertSet(new TurnFolding().Collapsed(BuildGraph(4), null), 0, 1, 2);
        AssertSet(new TurnFolding().Collapsed(BuildGraph(1), null));
        AssertSet(new TurnFolding().Collapsed(new FlowBuilder().Snapshot(), null));
    }

    [Fact]
    public void TheTurnOfTheSelectionStaysOpen()
    {
        FlowGraph g = BuildGraph(4);
        AssertSet(new TurnFolding().Collapsed(g, GraphSelection.ForStep("c1")), 0, 2);
        AssertSet(new TurnFolding().Collapsed(g, GraphSelection.ForTurn(2)), 0, 1);
    }

    [Fact]
    public void WhatTheUserTogglesBeatsTheDefaultAndSurvivesNewTurns()
    {
        FlowGraph g = BuildGraph(3);
        var folding = new TurnFolding();
        folding.Toggle(0, g, null);
        AssertSet(folding.Collapsed(g, null), 1);
        folding.Toggle(2, g, null);
        AssertSet(folding.Collapsed(g, null), 1, 2);
        Assert.True(folding.Collapsed(BuildGraph(4), null).Where(t => t < 3).ToHashSet().SetEquals([1, 2]));
        Assert.DoesNotContain(3, folding.Collapsed(BuildGraph(4), null));
    }

    [Fact]
    public void ASelectedTurnCanBeFoldedByHand()
    {
        FlowGraph g = BuildGraph(3);
        var folding = new TurnFolding();
        GraphSelection selection = GraphSelection.ForStep("c1");
        Assert.DoesNotContain(1, folding.Collapsed(g, selection));
        folding.Toggle(1, g, selection);
        Assert.Contains(1, folding.Collapsed(g, selection));
    }

    [Fact]
    public void ExpandAllCollapseAllAndReset()
    {
        FlowGraph g = BuildGraph(3);
        var folding = new TurnFolding();
        folding.ExpandAll(g);
        AssertSet(folding.Collapsed(g, null));
        folding.CollapseAll(g);
        AssertSet(folding.Collapsed(g, null), 0, 1, 2);
        folding.Reset();
        AssertSet(folding.Collapsed(g, null), 0, 1);
    }

    [Fact]
    public void WithoutAutoFoldingNothingIsFoldedUntilTheUserSaysSo()
    {
        FlowGraph g = BuildGraph(4);
        var folding = new TurnFolding();
        folding.SetAuto(false);
        AssertSet(folding.Collapsed(g, null));
        folding.Toggle(1, g, null);
        AssertSet(folding.Collapsed(g, null), 1);
        AssertSet(folding.Collapsed(BuildGraph(6), null), 1);
    }

    [Fact]
    public void FindsTheTurnOfAStepAndIgnoresUnknownOnes()
    {
        FlowGraph g = BuildGraph(3);
        Assert.Equal(2, TurnFolding.TurnOf(g, GraphSelection.ForStep("c2")));
        Assert.Equal(-1, TurnFolding.TurnOf(g, GraphSelection.ForStep("nope")));
        Assert.Equal(-1, TurnFolding.TurnOf(g, null));
    }
}
