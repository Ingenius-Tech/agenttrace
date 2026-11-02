using System.Diagnostics;
using System.Globalization;
using System.Text;
using AgentTrace.Core;
using AgentTrace.Diff;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Presentation.Text;

/// <summary>Turns the two versions of a file into the rows of a unified diff. Does not depend on Avalonia.</summary>
public static class DiffFormatter
{
    public static IReadOnlyList<DiffRow> Rows(FileVersions versions, string displayPath)
    {
        if (versions.UnavailableReason is { } reason)
        {
            return [DiffRow.Message(string.Format(CultureInfo.CurrentCulture, TextStrings.DiffContentUnavailableFormat, ReasonText.Describe(reason)))];
        }
        DiffResult diff = UnifiedDiffEngine.ComputeDiff(versions.Before, versions.After, displayPath);
        if (diff.Unavailable is not null) return [DiffRow.Message(string.Format(CultureInfo.CurrentCulture, TextStrings.DiffUnavailableFormat, ReasonText.Describe(diff.Unavailable.Value)))];
        if (diff.Additions == 0 && diff.Deletions == 0)
        {
            if (versions.Before is null && versions.After is not null) return [DiffRow.Message(TextStrings.DiffNewEmptyFile)];
            if (versions.Before is not null && versions.After is null) return [DiffRow.Message(TextStrings.DiffDeletedEmptyFile)];
            return [DiffRow.Message(TextStrings.DiffNoContentChanges)];
        }
        var rows = new List<DiffRow>
        {
            DiffRow.Header("--- " + (versions.Before is null ? "/dev/null" : "a/" + displayPath)),
            DiffRow.Header("+++ " + (versions.After is null ? "/dev/null" : "b/" + displayPath)),
            DiffRow.Header("(+" + diff.Additions + " −" + diff.Deletions + ")"),
        };
        foreach (DiffHunk hunk in diff.Hunks)
        {
            // In the unified format an empty side is numbered with the previous line (-0,0 in a new file)
            int oldStart = hunk.OldCount == 0 ? hunk.OldStart - 1 : hunk.OldStart;
            int newStart = hunk.NewCount == 0 ? hunk.NewStart - 1 : hunk.NewStart;
            rows.Add(new DiffRow(DiffRow.RowKind.Hunk, null, null,
                "@@ -" + oldStart + "," + hunk.OldCount + " +" + newStart + "," + hunk.NewCount + " @@"));
            foreach (DiffLine line in hunk.Lines)
            {
                DiffRow.RowKind kind = line.Type switch
                {
                    LineType.Unchanged => DiffRow.RowKind.Context,
                    LineType.Addition => DiffRow.RowKind.Add,
                    LineType.Deletion => DiffRow.RowKind.Del,
                    _ => throw new UnreachableException(),
                };
                rows.Add(new DiffRow(kind, line.OldNumber, line.NewNumber, line.Content));
            }
        }
        return rows;
    }

    /// <summary>The diff as plain text (a lone message has no trailing line break).</summary>
    public static string Format(FileVersions versions, string displayPath) => Text(Rows(versions, displayPath));

    public static string Text(IReadOnlyList<DiffRow> rows)
    {
        if (rows.Count == 1 && rows[0].Kind == DiffRow.RowKind.Message) return rows[0].Text;
        var output = new StringBuilder();
        foreach (DiffRow row in rows) output.Append(row.DisplayText).Append('\n');
        return output.ToString();
    }
}
