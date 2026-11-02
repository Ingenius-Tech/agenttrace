using System.Diagnostics;
using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Presentation.Resources;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Text;

/// <summary>Text of one line of the change list. Does not depend on Avalonia.</summary>
public static class ChangeFormatter
{
    private const int Snippet = 60;

    public static string Row(FilePath projectDir, ChangeEntry entry)
    {
        FileChange change = entry.Change;
        return ChangeStyle.Symbol(change.Kind) + "  " + Relative(projectDir, change.Path) + "    " + AttributionText(entry.Attribution);
    }

    /// <summary>The path relative to the project with <c>/</c>, or the whole path (also with <c>/</c>) if it is outside.</summary>
    public static string Relative(FilePath projectDir, FilePath file)
    {
        // Java's Path.relativize gives "" for the root itself; GetRelativePath gives "."
        string text = file == projectDir ? "" : file.IsUnder(projectDir) ? file.RelativeTo(projectDir) : file.Value;
        return text.Replace('\\', '/');
    }

    public static string AttributionText(Attribution a)
    {
        IReadOnlyList<ToolCall> calls = a.Calls;
        return a.Confidence switch
        {
            Confidence.Exact => "← " + Describe(calls[0]),
            Confidence.Probable => "≈ " + Describe(calls[0]),
            Confidence.Ambiguous => string.Format(CultureInfo.CurrentCulture, TextStrings.AttributionAmbiguousFormat, calls.Count, string.Join(", ", DistinctInOrder(calls.Select(c => c.ToolName)))),
            Confidence.None => TextStrings.AttributionExternal,
            Confidence.Undated => TextStrings.AttributionUndated,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>Java's <c>Stream.distinct</c> keeps the order of first appearance.</summary>
    private static List<string> DistinctInOrder(IEnumerable<string> names)
    {
        var seen = new HashSet<string>();
        var output = new List<string>();
        foreach (string name in names)
        {
            if (seen.Add(name)) output.Add(name);
        }
        return output;
    }

    private static string Describe(ToolCall call)
    {
        string input = JavaText.OneLine(call.InputJson, Snippet);
        return call.ToolName + (input.Length == 0 ? "" : " · " + input);
    }
}
