using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Panorama;

/// <summary>
/// Resolves a focus into what the three panes should highlight: the related turns, steps, files and Areas rows. Pure: data in, data out.
/// The files of a step are those of <see cref="StepFiles"/> plus its <c>Target</c>.
/// </summary>
public static class FocusLinks
{
    public static FocusTargets Of(TraceFocus focus, FlowGraph flow, IReadOnlyList<ChangeEntry> changes, AreasMatrix matrix)
    {
        ILookup<string, ChangeEntry> owned = StepFiles.OwnedBy(changes);
        int? first = null;
        int? last = null;
        HashSet<string> calls = [];
        HashSet<FilePath> files = [];
        switch (focus.Kind)
        {
            case FocusKind.Step when focus.CallId is { } id:
                foreach (Batch batch in flow.Batches)
                {
                    foreach (Step step in batch.Steps.Where(s => s.CallId == id))
                    {
                        first = last = batch.TurnIndex;
                        AddStep(step, calls, files, owned);
                    }
                }
                break;
            case FocusKind.Turn when focus.Turn is { } turn && turn >= 0 && turn < flow.Turns.Count:
                first = last = turn;
                AddTurns(flow, turn, turn, calls, files, owned);
                break;
            case FocusKind.Task when focus.Task is { } task && task >= 0 && task < matrix.Snapshot.Tasks.Count:
                AreaTask range = matrix.Snapshot.Tasks[task];
                first = range.FirstTurn;
                last = range.LastTurn;
                AddTurns(flow, range.FirstTurn, range.LastTurn, calls, files, owned);
                break;
            case FocusKind.AreaCell when focus.Area is { } area && focus.Turn is { } cellTurn:
                first = last = cellTurn;
                files.UnionWith(matrix.FilesOf(area, cellTurn));
                // The steps of that turn that touched those files
                foreach (Batch batch in flow.Batches.Where(b => b.TurnIndex == cellTurn))
                {
                    foreach (Step step in batch.Steps)
                    {
                        HashSet<FilePath> own = [];
                        AddStep(step, [], own, owned);
                        if (own.Overlaps(files)) calls.Add(step.CallId);
                    }
                }
                break;
            case FocusKind.File when focus.File is { } file:
                foreach (Batch batch in flow.Batches)
                {
                    foreach (Step step in batch.Steps)
                    {
                        HashSet<FilePath> own = [];
                        AddStep(step, [], own, owned);
                        if (!own.Contains(file)) continue;
                        calls.Add(step.CallId);
                        first = first is { } f ? Math.Min(f, batch.TurnIndex) : batch.TurnIndex;
                        last = last is { } l ? Math.Max(l, batch.TurnIndex) : batch.TurnIndex;
                    }
                }
                files.Add(file);
                break;
            default:
                return FocusTargets.Empty;
        }
        return new FocusTargets(first, last, calls, files, matrix.RowsOf(files));
    }

    private static void AddTurns(FlowGraph flow, int first, int last, HashSet<string> calls, HashSet<FilePath> files, ILookup<string, ChangeEntry> owned)
    {
        foreach (Batch batch in flow.Batches.Where(b => b.TurnIndex >= first && b.TurnIndex <= last))
        {
            foreach (Step step in batch.Steps) AddStep(step, calls, files, owned);
        }
        for (int turn = first; turn <= last && turn < flow.Turns.Count; turn++) files.UnionWith(flow.Turns[turn].ExternalPaths);
    }

    private static void AddStep(Step step, HashSet<string> calls, HashSet<FilePath> files, ILookup<string, ChangeEntry> owned)
    {
        calls.Add(step.CallId);
        foreach (FileTouch touch in StepFiles.Of(step, owned).Files) files.Add(touch.Path);
        if (step.Target is { } target) files.Add(target);
    }
}
