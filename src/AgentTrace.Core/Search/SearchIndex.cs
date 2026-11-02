using System.Diagnostics;
using System.Globalization;
using System.Text;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Search;

/// <summary>What an index entry is.</summary>
public enum SearchKind { Request, Response, Thinking, Tool, File, Error }

public enum SearchLane { Any, Main, Subagent }

/// <summary>An entry: what was said or done, with its origin. <c>Turn</c> starts at 0; <c>Lane</c> is null in the main thread. <c>Truncated</c>: the text was indexed cut off.</summary>
public sealed record SearchEntry(SearchKind Kind, string Text, string? CallId, int Turn, string? Lane, DateTimeOffset? Time, FilePath? Path, bool Truncated);

/// <summary>What is asked: words (all must appear, ignoring case and accents) and combinable filters.</summary>
public sealed record SearchQuery(string Text, IReadOnlySet<SearchKind>? Kinds = null, SearchLane Lane = SearchLane.Any, int? Turn = null);

/// <summary>A result: the entry, a fragment of its text and which parts of the fragment match.</summary>
public sealed record SearchHit(SearchEntry Entry, string Snippet, IReadOnlyList<(int Start, int Length)> Highlights);

/// <summary>
/// In-memory index of what happened in the active session: it is fed event by event (the same ones the graph builders
/// receive) and queried with a linear search over normalized text (a session is a few MB). Single thread.
/// Turns are counted as in <c>KnowledgeBuilder</c>.
/// </summary>
public sealed class SearchIndex
{
    /// <summary>Limit of indexed text per entry (as in the detail pane).</summary>
    public const int MaxTextLength = 20_000;

    private const int Context = 40;

    private readonly List<(SearchEntry Entry, string Normalized)> _entries = [];
    private int _turns;
    private bool _turnOpen;

    public int Count => _entries.Count;

    public void Accept(AgentEvent evt)
    {
        bool main = evt.Header.SubagentId == null;
        switch (evt)
        {
            case UserMessage e:
                if (main) NewTurn();
                Add(evt, SearchKind.Request, e.Text);
                break;
            case TurnEnded:
                if (main) _turnOpen = false;
                break;
            case AssistantText e:
                Add(evt, SearchKind.Response, e.Text);
                break;
            case Thinking e:
                if (e.Text.Length > 0) Add(evt, SearchKind.Thinking, e.Text);
                break;
            case ToolCall e:
                Add(evt, SearchKind.Tool, e.ToolName + " " + e.Summary + " " + e.InputJson, callId: e.CallId);
                break;
            case ToolResult e:
                Add(evt, e.Error ? SearchKind.Error : SearchKind.Tool, e.Output, callId: e.CallId);
                break;
            case FileRead e:
                Add(evt, SearchKind.File, e.Path.Value, callId: e.CallId, path: e.Path);
                break;
            case FileChanged e:
                Add(evt, SearchKind.File, e.Path.Value, callId: e.CallId, path: e.Path);
                break;
            case SubagentStarted or TokenUsage or Skipped:
                break;
            default:
                throw new UnreachableException();
        }
    }

    public IReadOnlyList<SearchHit> Search(SearchQuery query, int limit = 500)
    {
        string[] words = Normalize(query.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];
        List<SearchHit> hits = [];
        foreach ((SearchEntry entry, string normalized) in _entries)
        {
            if (query.Kinds is { Count: > 0 } kinds && !kinds.Contains(entry.Kind)) continue;
            if (query.Lane == SearchLane.Main && entry.Lane is not null) continue;
            if (query.Lane == SearchLane.Subagent && entry.Lane is null) continue;
            if (query.Turn is { } turn && entry.Turn != turn) continue;
            if (!words.All(w => normalized.Contains(w, StringComparison.Ordinal))) continue;
            hits.Add(Snippet(entry, normalized, words));
            if (hits.Count >= limit) break;
        }
        return hits;
    }

    private void Add(AgentEvent evt, SearchKind kind, string text, string? callId = null, FilePath? path = null)
    {
        if (!_turnOpen) NewTurn();
        bool truncated = text.Length > MaxTextLength;
        if (truncated) text = text[..MaxTextLength];
        EventHeader h = evt.Header;
        var entry = new SearchEntry(kind, text, callId, _turns - 1, h.SubagentId, h.Timestamp, path, truncated);
        _entries.Add((entry, Normalize(text)));
    }

    private void NewTurn()
    {
        _turns++;
        _turnOpen = true;
    }

    private static SearchHit Snippet(SearchEntry entry, string normalized, string[] words)
    {
        int first = words.Min(w => normalized.IndexOf(w, StringComparison.Ordinal));
        int start = Math.Max(0, first - Context);
        int end = Math.Min(entry.Text.Length, first + words.Max(w => w.Length) + Context);
        // Each line break is replaced by one character (not ReplaceLineEndings: "\r\n" would become 1 character and the positions would be off)
        string snippet = FlattenLines(entry.Text[start..end]);
        string window = normalized[start..end];
        int shift = start > 0 ? 1 : 0;
        List<(int Start, int Length)> marks = [];
        foreach (string w in words)
        {
            for (int i = window.IndexOf(w, StringComparison.Ordinal); i >= 0; i = window.IndexOf(w, i + w.Length, StringComparison.Ordinal))
            {
                marks.Add((i + shift, w.Length));
            }
        }
        marks.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new SearchHit(entry, (start > 0 ? "…" : "") + snippet + (end < entry.Text.Length ? "…" : ""), marks);
    }

    private static string FlattenLines(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Lowercase and without accents, character by character (the length does not change, so the positions are valid for the original text).</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(NormalizeChar(c));
        return sb.ToString();
    }

    private static char NormalizeChar(char c)
    {
        char lower = char.ToLowerInvariant(c);
        if (lower < 128 || char.IsSurrogate(lower)) return lower; // an emoji is two halves that cannot be decomposed separately
        string decomposed = lower.ToString().Normalize(NormalizationForm.FormD);
        return CharUnicodeInfo.GetUnicodeCategory(decomposed[0]) == UnicodeCategory.NonSpacingMark ? lower : decomposed[0];
    }
}
