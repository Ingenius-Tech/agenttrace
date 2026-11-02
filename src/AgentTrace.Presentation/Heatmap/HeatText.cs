using System.Globalization;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Presentation.Heatmap;

/// <summary>Heatmap texts: the tooltip of a cell and the summary of the view. No Avalonia.</summary>
public static class HeatText
{
    public static string ErrorsUnavailable => MapStrings.HeatErrorsUnavailable;

    /// <summary>Path relative to the project (with "/"), size, counters and turns; for a folder, how many files it has and how many were touched.</summary>
    public static string Tooltip(HeatNode node, HeatTree tree)
    {
        string relative = node.Path == tree.Root.Path ? node.Name : node.Path.RelativeTo(tree.Root.Path).Replace('\\', '/');
        List<string> lines = [relative];
        FileActivity a = node.Activity;
        if (node.Directory)
        {
            lines.Add(FilesTouched(node.Files, node.TouchedFiles) + " · " + FileHeader.Size(node.Size));
        }
        else
        {
            lines.Add(node.Ghost ? MapStrings.HeatDeleted : FileHeader.Size(node.Size));
        }
        if (a.Total == 0)
        {
            lines.Add(MapStrings.HeatNoActivity);
            return string.Join("\n", lines);
        }
        List<string> parts = [];
        if (a.Reads > 0) parts.Add(Count(a.Reads, MapStrings.CountReads_One, MapStrings.CountReads_Other));
        if (a.Writes > 0) parts.Add(Count(a.Writes, MapStrings.CountWrites_One, MapStrings.CountWrites_Other) + Breakdown(a));
        if (a.Errors > 0) parts.Add(Count(a.Errors, MapStrings.CountErrors_One, MapStrings.CountErrors_Other));
        lines.Add(string.Join(" · ", parts));
        if (!node.Directory && node.TurnsTouched > 0) lines.Add(Count(node.TurnsTouched, MapStrings.CountTurns_One, MapStrings.CountTurns_Other));
        return string.Join("\n", lines);
    }

    /// <summary>One line for the footer of the map: files, touched, what the active kinds add up to, and the warnings.</summary>
    public static string Summary(HeatTree tree, ActivityFilter filter)
    {
        HeatNode root = tree.Root;
        FileActivity a = root.Activity;
        List<string> parts = [FilesTouched(root.Files, root.TouchedFiles)];
        if (filter.Reads) parts.Add(Count(a.Reads, MapStrings.CountReads_One, MapStrings.CountReads_Other));
        if (filter.Writes) parts.Add(Count(a.Writes, MapStrings.CountWrites_One, MapStrings.CountWrites_Other));
        if (filter.Errors) parts.Add(Count(a.Errors, MapStrings.CountErrors_One, MapStrings.CountErrors_Other));
        if (tree.UnplacedErrors > 0) parts.Add(Count(tree.UnplacedErrors, MapStrings.CountErrorsNoFile_One, MapStrings.CountErrorsNoFile_Other));
        if (tree.Truncated) parts.Add(MapStrings.HeatTruncated);
        return string.Join(" · ", parts);
    }

    /// <summary>" (2 created, 1 modified…)" with the count of each kind of write; empty if all are of the same kind.</summary>
    private static string Breakdown(FileActivity a)
    {
        List<string> kinds = [];
        if (a.Created > 0) kinds.Add(Count(a.Created, MapStrings.CountCreated_One, MapStrings.CountCreated_Other));
        if (a.Modified > 0) kinds.Add(Count(a.Modified, MapStrings.CountModified_One, MapStrings.CountModified_Other));
        if (a.Deleted > 0) kinds.Add(Count(a.Deleted, MapStrings.CountDeleted_One, MapStrings.CountDeleted_Other));
        if (a.External > 0) kinds.Add(Count(a.External, MapStrings.CountExternal_One, MapStrings.CountExternal_Other));
        return kinds.Count > 1 ? " (" + string.Join(", ", kinds) + ")" : "";
    }

    private static string FilesTouched(int files, int touched) => string.Format(CultureInfo.CurrentCulture, MapStrings.HeatFilesTouchedFormat,
        Count(files, MapStrings.CountFiles_One, MapStrings.CountFiles_Other), Count(touched, MapStrings.CountTouched_One, MapStrings.CountTouched_Other));

    private static string Count(int n, string one, string other) => string.Format(CultureInfo.CurrentCulture, Plural.Pick(n, one, other), LocalNumbers.Grouped(n));
}
