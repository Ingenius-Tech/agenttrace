using System.Diagnostics;
using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Core.Search;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Presentation.Search;

/// <summary>Where a result leads to: a step of the execution graph, a turn of the graph or a file in the viewer.</summary>
public abstract record JumpTarget
{
    public sealed record Step(string CallId) : JumpTarget;

    public sealed record Turn(int Index) : JumpTarget;

    public sealed record File(FilePath Path) : JumpTarget;

    /// <summary>A file leads to the viewer; a tool or its result, to its step; a message, to its turn.</summary>
    public static JumpTarget Of(SearchEntry entry) => entry.Kind switch
    {
        SearchKind.File when entry.Path is { } path => new File(path),
        SearchKind.Tool or SearchKind.Error or SearchKind.File when entry.CallId is { } call => new Step(call),
        SearchKind.Request or SearchKind.Response or SearchKind.Thinking or SearchKind.Tool or SearchKind.Error or SearchKind.File => new Turn(entry.Turn),
        _ => throw new UnreachableException(),
    };
}

/// <summary>The texts of the search window.</summary>
public static class SearchPresentation
{
    public static string KindLabel(SearchKind kind) => kind switch
    {
        SearchKind.Request => TextStrings.SearchKindRequest,
        SearchKind.Response => TextStrings.SearchKindResponse,
        SearchKind.Thinking => TextStrings.SearchKindThinking,
        SearchKind.Tool => TextStrings.SearchKindTool,
        SearchKind.File => TextStrings.SearchKindFile,
        SearchKind.Error => TextStrings.SearchKindError,
        _ => throw new UnreachableException(),
    };

    /// <summary>"Turn 3 · main thread · 10:00:01".</summary>
    public static string Where(SearchEntry entry)
    {
        string lane = entry.Lane is null
            ? TextStrings.SearchLaneMain
            : string.Format(CultureInfo.CurrentCulture, TextStrings.SearchLaneSubagentFormat, entry.Lane.Length > 8 ? entry.Lane[..8] : entry.Lane);
        List<string> parts = [string.Format(CultureInfo.CurrentCulture, TextStrings.SearchTurnFormat, entry.Turn + 1), lane];
        if (entry.Time is { } t) parts.Add(t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        if (entry.Truncated) parts.Add(TextStrings.SearchTruncated);
        return string.Join(" · ", parts);
    }

    public static string Summary(int shown, int limit) => shown == 0
        ? TextStrings.SearchNoResults
        : shown >= limit
            ? string.Format(CultureInfo.CurrentCulture, TextStrings.SearchMoreResultsFormat, limit)
            : string.Format(CultureInfo.CurrentCulture, Plural.Pick(shown, TextStrings.SearchResults_One, TextStrings.SearchResults_Other), shown);
}
