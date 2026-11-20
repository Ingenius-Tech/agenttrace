namespace AgentTrace.Core.Areas;

/// <summary>An area of a repository (areas of different repositories can share an id). <c>Repo</c> is any stable text that identifies the repository.</summary>
public readonly record struct AreaRef(string Repo, string Id);

/// <summary>How a file is classified, for the summary of a task. <c>SharedModule</c>: its module is marked <c>shared</c> in the rules.</summary>
public sealed record AreaClassification(string Repo, AreaAssignment Assignment, bool SharedModule);

/// <summary>
/// Warnings about a task. <c>UntestedChange</c>: something was changed and no file of kind <c>test</c> was touched. <c>CrossModule</c>: it changed
/// several modules. <c>SharedTouched</c>: it changed a file of kind <c>contract</c> or of a module marked shared.
/// </summary>
[Flags]
public enum AreaBadges { None = 0, UntestedChange = 1, CrossModule = 2, SharedTouched = 4 }

/// <summary>
/// What a task did. <c>Files</c>: files with some activity; <c>WrittenFiles</c>: those that were changed. <c>Layers</c>/<c>Modules</c>: the areas
/// touched (read or changed), in order of first appearance. <c>LayerOrder</c>: the layers in the order they were first changed, which shows how the
/// change propagated. <c>WrittenModules</c>: the modules that were changed. <c>TestsKnown</c>: whether the rules define a <c>test</c> kind; without it
/// <c>UntestedChange</c> is never set (it is "not computed", not "fine").
/// </summary>
public sealed record AreaTaskSummary(int TaskIndex, int Files, int WrittenFiles, ValueList<AreaRef> Layers, ValueList<AreaRef> Modules,
    ValueList<AreaRef> LayerOrder, ValueList<AreaRef> WrittenModules, AreaBadges Badges, bool TestsKnown);

/// <summary>Builds the <see cref="AreaTaskSummary"/> of each task from the activity and a classifier of files.</summary>
public static class AreaTaskSummaries
{
    /// <summary>Kind id with a meaning: tests (see <see cref="AreaBadges.UntestedChange"/>).</summary>
    public const string TestKindId = "test";

    /// <summary>Kind id with a meaning: shared contracts (see <see cref="AreaBadges.SharedTouched"/>).</summary>
    public const string ContractKindId = "contract";

    /// <summary>A task that changes this many modules (or more) gets <see cref="AreaBadges.CrossModule"/>.</summary>
    public const int CrossModuleThreshold = 2;

    /// <param name="classify">the areas of a file, or null if no repository contains it</param>
    /// <param name="testKindDefined">whether some rules define a kind with the id <see cref="TestKindId"/></param>
    public static ValueList<AreaTaskSummary> Of(ValueList<AreaTask> tasks, AreaActivity activity, Func<FilePath, AreaClassification?> classify, bool testKindDefined)
    {
        List<AreaTaskSummary> summaries = [];
        foreach (AreaTask task in tasks)
        {
            HashSet<FilePath> files = [];
            HashSet<FilePath> written = [];
            List<AreaRef> layers = [];
            List<AreaRef> modules = [];
            List<AreaRef> layerOrder = [];
            List<AreaRef> writtenModules = [];
            bool wroteNonTest = false, touchedTest = false, touchedShared = false;
            void Note(List<AreaRef> list, AreaClassification c, string? id)
            {
                if (id is not null && !list.Contains(new AreaRef(c.Repo, id))) list.Add(new AreaRef(c.Repo, id));
            }

            foreach (AreaTurnActivity turn in activity.Turns.Where(t => t.Turn >= task.FirstTurn && t.Turn <= task.LastTurn))
            {
                // Changes first, in the order they happened, so the layers come out in propagation order
                foreach (AreaFileActivity file in turn.Files.OrderBy(f => f.WriteOrder ?? int.MaxValue))
                {
                    files.Add(file.Path);
                    bool wrote = file.Writes + file.External > 0;
                    if (wrote) written.Add(file.Path);
                    if (classify(file.Path) is not { } c) continue;
                    AreaAssignment a = c.Assignment;
                    Note(layers, c, a.Layer);
                    Note(modules, c, a.Module);
                    bool isTest = a.Kind == TestKindId;
                    if (isTest) touchedTest = true;
                    if (!wrote) continue;
                    Note(layerOrder, c, a.Layer);
                    Note(writtenModules, c, a.Module);
                    if (!isTest) wroteNonTest = true;
                    if (a.Kind == ContractKindId || c.SharedModule) touchedShared = true;
                }
            }

            AreaBadges badges = AreaBadges.None;
            if (testKindDefined && wroteNonTest && !touchedTest) badges |= AreaBadges.UntestedChange;
            if (writtenModules.Count >= CrossModuleThreshold) badges |= AreaBadges.CrossModule;
            if (touchedShared) badges |= AreaBadges.SharedTouched;
            summaries.Add(new AreaTaskSummary(task.Index, files.Count, written.Count, layers.ToValueList(), modules.ToValueList(), layerOrder.ToValueList(),
                writtenModules.ToValueList(), badges, testKindDefined));
        }
        return summaries.ToValueList();
    }
}
