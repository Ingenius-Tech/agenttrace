using AgentTrace.Core;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class GraphFilterTest
{
    private static Step MakeStep(ToolKind kind, StepState state) =>
        new("c", "T", kind, "", "{}", state, null, DateTimeOffset.UnixEpoch, null, ValueList<FileTouch>.Empty, null);

    [Fact]
    public void NoFilterDimsNothing()
    {
        Assert.False(GraphFilter.None.Active);
        foreach (ToolKind kind in Enum.GetValues<ToolKind>())
        {
            foreach (StepState state in Enum.GetValues<StepState>()) Assert.False(GraphFilter.None.Dims(MakeStep(kind, state)));
        }
        Assert.False(GraphFilter.None.DimsFoldedTurn(0));
    }

    [Fact]
    public void OnlyErrorsDimsEveryStepThatDidNotFail()
    {
        var f = new GraphFilter(true, false);
        Assert.True(f.Active);
        Assert.False(f.Dims(MakeStep(ToolKind.Execute, StepState.Error)));
        Assert.True(f.Dims(MakeStep(ToolKind.Execute, StepState.Ok)));
        Assert.True(f.Dims(MakeStep(ToolKind.Execute, StepState.Running)));
        Assert.True(f.Dims(MakeStep(ToolKind.Read, StepState.NoResult)));
        Assert.True(f.DimsFoldedTurn(0));
        Assert.False(f.DimsFoldedTurn(2));
    }

    [Fact]
    public void HideReadsDimsOnlyReads()
    {
        var f = new GraphFilter(false, true);
        Assert.True(f.Dims(MakeStep(ToolKind.Read, StepState.Ok)));
        Assert.True(f.Dims(MakeStep(ToolKind.Read, StepState.Error)), "a read that failed is still a read");
        Assert.False(f.Dims(MakeStep(ToolKind.Write, StepState.Ok)));
        Assert.False(f.DimsFoldedTurn(0));
    }

    [Fact]
    public void CombinedFiltersAddUp()
    {
        var f = new GraphFilter(true, true);
        Assert.False(f.Dims(MakeStep(ToolKind.Write, StepState.Error)));
        Assert.True(f.Dims(MakeStep(ToolKind.Read, StepState.Error)));
        Assert.Equal(new GraphFilter(true, true), f);
    }
}
