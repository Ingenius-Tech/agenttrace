using AgentTrace.Core.Flow;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Graph;

/// <summary>
/// Graph filters. They dim what is not of interest instead of removing it: the structure (order, columns) does not change
/// and what is dimmed stays visible and selectable.
/// <c>OnlyErrors</c>: dims every step that did not fail, and folded turns without errors.
/// <c>HideReads</c>: dims the read steps.
/// </summary>
public sealed record GraphFilter(bool OnlyErrors, bool HideReads)
{
    public static readonly GraphFilter None = new(false, false);

    public bool Active => OnlyErrors || HideReads;

    public bool Dims(Step step) => (OnlyErrors && step.State != StepState.Error) || (HideReads && step.Kind == ToolKind.Read);

    /// <summary>A folded turn is dimmed if only errors are requested and it has none.</summary>
    public bool DimsFoldedTurn(int errors) => OnlyErrors && errors == 0;
}
