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
}
