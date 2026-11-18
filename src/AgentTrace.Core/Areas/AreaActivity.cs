using AgentTrace.Core.Flow;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Areas;

/// <summary>
/// What happened to a file in a turn. <c>Reads</c>: reads by the agent. <c>Writes</c>: changes made by the agent (created, modified or deleted).
/// <c>External</c>: changes nothing in the trace explains. <c>Errors</c>: failed calls whose target is this file. <c>Order</c>: position of the first
/// touch within the turn (calls in time order; changes that no call explains come after all of them), to order what happened first.
/// </summary>
public sealed record AreaFileActivity(FilePath Path, int Reads, int Writes, int External, int Errors, int Order);

/// <summary>The files touched in a turn, ordered by first touch. <c>Turn</c> is a turn index of the flow (it may be 0 with no turns in the flow, see <see cref="AreaActivityBuilder"/>).</summary>
public sealed record AreaTurnActivity(int Turn, ValueList<AreaFileActivity> Files);

/// <summary>
/// Activity per turn and file, only for turns and files that have some. The stored unit is the file, not the area, so regrouping by another
/// dimension or after a rules change only classifies the files again.
/// </summary>
public sealed record AreaActivity(ValueList<AreaTurnActivity> Turns)
{
    public static readonly AreaActivity Empty = new(ValueList<AreaTurnActivity>.Empty);
}

/// <summary>
/// Builds the <see cref="AreaActivity"/> from the same sources as the Knowledge graph: the trace (the flow) and what git reports with its
/// attribution. Follows the same files (<see cref="ProjectPaths.IsFollowed"/>) and never counts a change twice.
/// </summary>
public static class AreaActivityBuilder
{
    private const int AfterEverySteps = int.MaxValue;

    /// <summary>
    /// Sources: the files of each step (reads and agent changes), the target of a failed step (errors), the external paths of each turn, and
    /// the git changes. A git change of a path the trace already reports as changed is ignored. Otherwise it is an agent write in the turn of
    /// its attributed call (for several candidates, the earliest, once); with no call it is external, in the turn whose time range contains
    /// its modification, else in the last one (also if undated). With no turns, it goes to turn 0.
    /// </summary>
    public static AreaActivity Build(FlowGraph flow, IReadOnlyList<ChangeEntry> changes, FilePath projectRoot)
    {
        Dictionary<(int Turn, FilePath Path), Counts> counts = [];
        void Add(int turn, FilePath path, int order, int reads = 0, int writes = 0, int external = 0, int errors = 0)
        {
            if (!ProjectPaths.IsFollowed(projectRoot, path)) return;
            counts.TryGetValue((turn, path), out Counts c);
            counts[(turn, path)] = c.Plus(order, reads, writes, external, errors);
        }

        // Steps of each turn in time order (a stable sort: without timestamps the order of the flow is kept)
        Dictionary<string, (int Turn, int Position)> positions = [];
        HashSet<FilePath> traceChanged = [];
        foreach (IGrouping<int, (FlowGraph.Step Step, int Turn)> group in flow.Batches
                     .SelectMany(b => b.Steps.Select(s => (Step: s, Turn: b.TurnIndex))).GroupBy(x => x.Turn).OrderBy(g => g.Key))
        {
            int position = 0;
            foreach ((FlowGraph.Step step, int turn) in group.OrderBy(x => x.Step.Start ?? DateTimeOffset.MinValue))
            {
                positions[step.CallId] = (turn, position);
                foreach (FlowGraph.FileTouch touch in step.Files)
                {
                    if (touch.Kind is null) Add(turn, touch.Path, position, reads: 1);
                    else
                    {
                        Add(turn, touch.Path, position, writes: 1);
                        traceChanged.Add(touch.Path);
                    }
                }
                if (step.State == FlowGraph.StepState.Error && step.Target is { } target) Add(turn, target, position, errors: 1);
                position++;
            }
        }
        foreach (FlowGraph.Turn turn in flow.Turns)
        {
            foreach (FilePath path in turn.ExternalPaths)
            {
                Add(turn.Index, path, AfterEverySteps, external: 1);
                traceChanged.Add(path);
            }
        }

        int lastTurn = Math.Max(flow.Turns.Count - 1, 0);
        foreach (ChangeEntry entry in changes)
        {
            FilePath path = entry.Change.Path;
            if (traceChanged.Contains(path)) continue;
            (int Turn, int Position)? at = entry.Attribution.Confidence is Confidence.Exact or Confidence.Probable or Confidence.Ambiguous
                ? EarliestCall(entry.Attribution.Calls, positions)
                : null;
            if (at is { } call) Add(call.Turn, path, call.Position, writes: 1);
            else Add(TurnOf(flow, entry.Change.ModifiedAt) ?? lastTurn, path, AfterEverySteps, external: 1);
        }

        List<AreaTurnActivity> turns = [];
        foreach (IGrouping<int, KeyValuePair<(int Turn, FilePath Path), Counts>> turn in counts.GroupBy(kv => kv.Key.Turn).OrderBy(g => g.Key))
        {
            List<AreaFileActivity> files = turn
                .Select(kv => new AreaFileActivity(kv.Key.Path, kv.Value.Reads, kv.Value.Writes, kv.Value.External, kv.Value.Errors, kv.Value.Order))
                .OrderBy(f => f.Order).ThenBy(f => f.Path.Value, StringComparer.Ordinal).ToList();
            turns.Add(new AreaTurnActivity(turn.Key, files.ToValueList()));
        }
        return new AreaActivity(turns.ToValueList());
    }

    private static (int Turn, int Position)? EarliestCall(ValueList<ToolCall> calls, Dictionary<string, (int Turn, int Position)> positions)
    {
        (int Turn, int Position)? best = null;
        foreach (ToolCall call in calls)
        {
            if (!positions.TryGetValue(call.CallId, out (int Turn, int Position) p)) continue;
            if (best is not { } b || p.Turn < b.Turn || (p.Turn == b.Turn && p.Position < b.Position)) best = p;
        }
        return best;
    }

    /// <summary>The first turn whose time range contains <paramref name="time"/>, or null (also without a time).</summary>
    private static int? TurnOf(FlowGraph flow, DateTimeOffset? time)
    {
        if (time is not { } t) return null;
        foreach (FlowGraph.Turn turn in flow.Turns)
        {
            if (turn.Start is { } start && t >= start && t <= (turn.End ?? start)) return turn.Index;
        }
        return null;
    }

    private readonly record struct Counts(int Reads, int Writes, int External, int Errors, int Order)
    {
        public Counts Plus(int order, int reads, int writes, int external, int errors) => new(
            Reads + reads, Writes + writes, External + external, Errors + errors,
            Reads + Writes + External + Errors == 0 ? order : Math.Min(Order, order));
    }
}
