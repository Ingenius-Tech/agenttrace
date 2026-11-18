using System.Globalization;
using AgentTrace.Core.Areas;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Presentation.Areas;

/// <summary>Areas map texts: turns the data of <c>Core</c> and of the matrix into localized text. No Avalonia.</summary>
public static class AreaText
{
    public static string Group(AreaDimension dimension) => dimension switch
    {
        AreaDimension.Layer => AreaStrings.GroupLayer,
        AreaDimension.Module => AreaStrings.GroupModule,
        AreaDimension.Kind => AreaStrings.GroupKind,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    /// <summary>"Task 3", or "Task 3 (no prompt)" for a task that starts with a continuation turn. Numbers start at 1.</summary>
    public static string TaskTitle(AreaTask task) => string.Format(CultureInfo.CurrentCulture,
        task.HasPrompt ? AreaStrings.TaskFormat : AreaStrings.TaskNoPromptFormat, LocalNumbers.Grouped(task.Index + 1));

    public static string TurnTitle(int turn) => string.Format(CultureInfo.CurrentCulture, AreaStrings.TurnFormat, LocalNumbers.Grouped(turn + 1));

    public static string CellTitle(string areaName, int turn) =>
        string.Format(CultureInfo.CurrentCulture, AreaStrings.CellTitleFormat, areaName, LocalNumbers.Grouped(turn + 1));

    /// <summary>"3 reads · 1 write · 2 errors" for the active kinds that have something.</summary>
    public static string Counts(FileActivity a, ActivityFilter filter)
    {
        List<string> parts = [];
        if (filter.Reads && a.Reads > 0) parts.Add(HeatText.Count(a.Reads, MapStrings.CountReads_One, MapStrings.CountReads_Other));
        if (filter.Writes && a.Writes > 0) parts.Add(HeatText.Count(a.Writes, MapStrings.CountWrites_One, MapStrings.CountWrites_Other));
        if (filter.Errors && a.Errors > 0) parts.Add(HeatText.Count(a.Errors, MapStrings.CountErrors_One, MapStrings.CountErrors_Other));
        return string.Join(" · ", parts);
    }

    public static string MoreFiles(int count) => HeatText.Count(count, AreaStrings.MoreFiles_One, AreaStrings.MoreFiles_Other);

    /// <summary>Tooltip of a row header: the name and what the agent did there.</summary>
    public static string RowTooltip(string name, FileActivity total, int files, ActivityFilter filter) =>
        name + "\n" + HeatText.Count(files, MapStrings.CountFiles_One, MapStrings.CountFiles_Other) + " · " + Counts(total, filter);

    /// <summary>"3 files · 2 layers · 1 module": what the task touched.</summary>
    public static string TaskCounts(AreaTaskSummary s) => string.Join(" · ",
        HeatText.Count(s.Files, MapStrings.CountFiles_One, MapStrings.CountFiles_Other),
        HeatText.Count(s.Layers.Count, AreaStrings.CountLayers_One, AreaStrings.CountLayers_Other),
        HeatText.Count(s.Modules.Count, AreaStrings.CountModules_One, AreaStrings.CountModules_Other));

    /// <summary>"First changes: Data → Domain → API": the layers in the order the agent first changed them; null if it changed none.</summary>
    public static string? TaskOrder(AreaTaskSummary s, Func<AreaRef, string> name) => s.LayerOrder.Count == 0
        ? null
        : string.Format(CultureInfo.CurrentCulture, AreaStrings.SummaryOrderFormat, string.Join(" → ", s.LayerOrder.Select(name)));

    /// <summary>The text of each badge the task has, plus, when tests cannot be checked, the note that says so.</summary>
    public static IReadOnlyList<string> BadgeTexts(AreaTaskSummary s)
    {
        List<string> texts = [];
        if (s.Badges.HasFlag(AreaBadges.UntestedChange)) texts.Add(AreaStrings.BadgeUntested);
        if (s.Badges.HasFlag(AreaBadges.CrossModule)) texts.Add(string.Format(CultureInfo.CurrentCulture, AreaStrings.BadgeCrossModuleFormat, LocalNumbers.Grouped(s.WrittenModules.Count)));
        if (s.Badges.HasFlag(AreaBadges.SharedTouched)) texts.Add(AreaStrings.BadgeShared);
        if (!s.TestsKnown) texts.Add(AreaStrings.BadgeNotComputed);
        return texts;
    }

    /// <summary>Where the rules come from: the file, the suggested ones, or the suggested ones because the file is invalid (some of each: counted).</summary>
    public static string Source(AreaRepoSet repos)
    {
        int valid = repos.Repos.Count(r => r.Source == AreaSource.File);
        if (repos.Repos.Count == 0) return "";
        if (valid == repos.Repos.Count) return AreaStrings.SourceFile;
        if (valid > 0) return string.Format(CultureInfo.CurrentCulture, AreaStrings.SourceMixedFormat, LocalNumbers.Grouped(valid), LocalNumbers.Grouped(repos.Repos.Count));
        return repos.Repos.Any(r => r.Source == AreaSource.Invalid) ? AreaStrings.SourceInvalid : AreaStrings.SourceSuggested;
    }

    /// <summary>"Layer 100% · Module 100% · Kind 62%": the coverage of each dimension.</summary>
    public static string CoverageItems(AreaCoverage coverage) => string.Join(" · ", coverage.Dimensions.Select(d =>
        string.Format(CultureInfo.CurrentCulture, AreaStrings.CoverageItemFormat, Group(d.Dimension),
            string.Format(CultureInfo.CurrentCulture, AreaStrings.CoveragePercentFormat, LocalNumbers.Grouped(d.Percent)))));

    /// <summary>The error lines of every repository whose rules file is invalid, at most <paramref name="max"/>; the rest as a "+N more" line.</summary>
    public static IReadOnlyList<string> ErrorLines(AreaRepoSet repos, int max)
    {
        List<string> all = [];
        bool several = repos.Repos.Count > 1;
        foreach (AreaRepo repo in repos.Repos.Where(r => r.Source == AreaSource.Invalid))
            all.AddRange(repo.Errors.Select(e => several ? string.Format(CultureInfo.CurrentCulture, AreaStrings.ErrorAreaFormat, repo.Name, AreaErrorText.Of(e)) : AreaErrorText.Of(e)));
        if (all.Count <= max) return all;
        List<string> shown = all.GetRange(0, max);
        shown.Add(HeatText.Count(all.Count - max, AreaStrings.ErrorsMore_One, AreaStrings.ErrorsMore_Other));
        return shown;
    }
}
