using AgentTrace.Core;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Graph;

/// <summary>
/// The files of a step: those its call declares (<c>Files</c>) plus the ones git attributes to that single call with enough confidence
/// (<c>ViaGit</c>, the part the trace does not declare). One place for the rule, used by the step detail and by the Panorama's linked focus.
/// </summary>
public sealed record StepFiles(ValueList<FileTouch> Files, ValueList<FilePath> ViaGit)
{
    /// <summary>The changes git attributes to exactly one call (Exact or Probable), by that call.</summary>
    public static ILookup<string, ChangeEntry> OwnedBy(IReadOnlyList<ChangeEntry> changes) =>
        changes.Where(e => (e.Attribution.Confidence == Confidence.Exact || e.Attribution.Confidence == Confidence.Probable) && e.Attribution.Calls.Count == 1)
            .ToLookup(e => e.Attribution.Calls[0].CallId);

    public static StepFiles Of(Step step, IReadOnlyList<ChangeEntry> changes) => Of(step, OwnedBy(changes));

    public static StepFiles Of(Step step, ILookup<string, ChangeEntry> owned)
    {
        var files = new List<FileTouch>(step.Files);
        var viaGit = new List<FilePath>();
        foreach (ChangeEntry entry in owned[step.CallId])
        {
            FilePath path = entry.Change.Path;
            if (files.Any(f => f.Path == path)) continue;
            files.Add(new FileTouch(path, entry.Change.Kind));
            viaGit.Add(path);
        }
        return new StepFiles(files.ToValueList(), viaGit.ToValueList());
    }
}
