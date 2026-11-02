using AgentTrace.Core.Flow;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Graph;

/// <summary>
/// Which turns are shown folded. By default everything is folded except the last turn and the one containing the
/// selection; what the user folds or unfolds by hand takes precedence. So a long session fits on screen and the
/// turn in progress is always fully visible. No Avalonia.
/// </summary>
public sealed class TurnFolding
{
    private readonly Dictionary<int, bool> _overrides = [];
    private bool _auto = true;

    /// <summary>No automatic folding (everything unfolded except what the user folds). It is the <c>--graph-collapse=none</c> argument.</summary>
    public void SetAuto(bool auto) => _auto = auto;

    public IReadOnlySet<int> Collapsed(FlowGraph graph, GraphSelection? selection)
    {
        int last = graph.Turns.Count - 1;
        int selected = TurnOf(graph, selection);
        var output = new HashSet<int>();
        for (int t = 0; t <= last; t++)
        {
            if (_overrides.TryGetValue(t, out bool forced) ? forced : _auto && t != last && t != selected) output.Add(t);
        }
        return output;
    }

    /// <summary>Inverts the current state of the turn (what the user sees) and pins it.</summary>
    public void Toggle(int turn, FlowGraph graph, GraphSelection? selection) =>
        _overrides[turn] = !Collapsed(graph, selection).Contains(turn);

    public void ExpandAll(FlowGraph graph)
    {
        for (int t = 0; t < graph.Turns.Count; t++) _overrides[t] = false;
    }

    public void CollapseAll(FlowGraph graph)
    {
        for (int t = 0; t < graph.Turns.Count; t++) _overrides[t] = true;
    }

    /// <summary>Goes back to the default rule.</summary>
    public void Reset() => _overrides.Clear();

    /// <summary>The turn the selection belongs to, or -1.</summary>
    internal static int TurnOf(FlowGraph graph, GraphSelection? selection)
    {
        if (selection is null) return -1;
        if (!selection.IsStep) return selection.TurnIndex;
        int max = Math.Max(graph.Turns.Count - 1, 0);
        foreach (Batch batch in graph.Batches)
        {
            foreach (Step step in batch.Steps)
            {
                if (step.CallId == selection.CallId) return Math.Min(batch.TurnIndex, max);
            }
        }
        return -1;
    }
}
