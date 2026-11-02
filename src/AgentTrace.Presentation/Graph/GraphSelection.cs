using System.Globalization;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.Flow.FlowGraph;
using static AgentTrace.Presentation.Graph.GraphGeometry;

namespace AgentTrace.Presentation.Graph;

/// <summary>What is selected in the graph: a step (<c>CallId</c>) or a whole turn (<c>CallId</c> null).</summary>
public sealed record GraphSelection(string? CallId, int TurnIndex)
{
    public static GraphSelection ForStep(string callId) => new(callId, -1);

    public static GraphSelection ForTurn(int index) => new(null, index);

    public bool IsStep => CallId is not null;

    /// <summary>The selection matching a box: the step, or the turn if it is its prompt or its answer.</summary>
    public static GraphSelection Of(Box box) => box.CallId is not null ? ForStep(box.CallId) : ForTurn(box.TurnIndex);

    /// <summary>
    /// Interprets a command-line argument: <c>&lt;callId&gt;</c>, <c>turn:&lt;n&gt;</c>, <c>last</c> (last step) or
    /// <c>last-error</c>. Null if it is not understood or does not exist in the graph.
    /// </summary>
    public static GraphSelection? Parse(string? spec, FlowGraph graph)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        string s = spec.Trim();
        if (s.StartsWith("turn:", StringComparison.Ordinal))
        {
            // Integer.parseInt: optional sign and digits, no spaces or separators
            if (!int.TryParse(s[5..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n)) return null;
            return n >= 0 && n < graph.Turns.Count ? ForTurn(n) : null;
        }
        if (s == "last" || s == "last-error")
        {
            bool errorsOnly = s == "last-error";
            Step? found = null;
            foreach (Batch b in graph.Batches)
            {
                foreach (Step step in b.Steps)
                {
                    if (errorsOnly && step.State != StepState.Error) continue;
                    // The last in time, not the last lane: subagents go at the end of the list.
                    if (found is null || step.Start is null || found.Start is null || !(step.Start.Value < found.Start.Value)) found = step;
                }
            }
            return found is null ? null : ForStep(found.CallId);
        }
        Step? byId = graph.FindStep(s);
        return byId is null ? null : ForStep(byId.CallId);
    }
}
