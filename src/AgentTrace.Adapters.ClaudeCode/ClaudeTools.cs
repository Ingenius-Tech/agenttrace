using System.Text.Json;
using System.Text.RegularExpressions;
using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode;

/// <summary>
/// What this adapter knows about Claude Code's tools: the kind of each one and the short text that describes it.
/// The core does not know these names; it only receives the kind and the summary inside each call.
/// </summary>
internal static partial class ClaudeTools
{
    private const int MaxSummary = 120;

    private static readonly HashSet<string> ReadTools = ["Read", "NotebookRead", "WebFetch"];
    private static readonly HashSet<string> WriteTools = ["Write", "Edit", "MultiEdit", "NotebookEdit"];
    private static readonly HashSet<string> ExecuteTools = ["Bash", "PowerShell"];
    private static readonly HashSet<string> SearchTools = ["Grep", "Glob", "WebSearch"];
    private static readonly HashSet<string> DelegateTools = ["Agent", "Task"];

    /// <summary>Tools that write files: the result says which one.</summary>
    public static bool WritesFiles(string toolName) => WriteTools.Contains(toolName);

    public static AgentEvent.ToolKind KindOf(string toolName)
    {
        if (ReadTools.Contains(toolName)) return AgentEvent.ToolKind.Read;
        if (WriteTools.Contains(toolName)) return AgentEvent.ToolKind.Write;
        if (ExecuteTools.Contains(toolName) || toolName.Contains("ctx_execute", StringComparison.Ordinal)
            || toolName.Contains("ctx_batch_execute", StringComparison.Ordinal)) return AgentEvent.ToolKind.Execute;
        if (SearchTools.Contains(toolName) || toolName.Contains("ctx_search", StringComparison.Ordinal)) return AgentEvent.ToolKind.Search;
        if (DelegateTools.Contains(toolName)) return AgentEvent.ToolKind.Delegate;
        return AgentEvent.ToolKind.Other;
    }

    /// <summary>Short one-line text with the most significant part of the input (path, command, pattern, description); may be empty.</summary>
    public static string Summary(AgentEvent.ToolKind kind, JsonElement input)
    {
        string[] keys = kind switch
        {
            AgentEvent.ToolKind.Read or AgentEvent.ToolKind.Write => ["file_path", "notebook_path", "path", "url"],
            AgentEvent.ToolKind.Execute => ["command", "code", "commands"],
            AgentEvent.ToolKind.Search => ["pattern", "query", "queries", "path"],
            AgentEvent.ToolKind.Delegate => ["description", "subagent_type", "prompt"],
            _ => ["description", "file_path", "path", "command", "pattern", "query", "url"],
        };
        foreach (string key in keys)
        {
            string text = TextOf(input.Path(key));
            if (text.Length > 0) return Shorten(text);
        }
        return "";
    }

    private static string TextOf(JsonElement node)
    {
        if (node.IsTextual()) return node.AsText();
        if (node.IsArray() && node.Size() > 0) return TextOf(node.First());
        return "";
    }

    private static string Shorten(string text)
    {
        string stripped = JavaText.Strip(text);
        int end = stripped.AsSpan().IndexOfAny('\n', '\r');
        string firstLine = JavaText.Strip(AsciiWhitespace().Replace(end >= 0 ? stripped[..end] : stripped, " "));
        return firstLine.Length > MaxSummary ? firstLine[..MaxSummary] + "…" : firstLine;
    }

    /// <summary>Java's <c>\s</c> (without Unicode mode): only the space, \t, \n, \x0B, \f and \r.</summary>
    [GeneratedRegex("[ \\t\\n\\x0B\\f\\r]+")]
    private static partial Regex AsciiWhitespace();
}

/// <summary>The few Java <c>String</c> methods whose definition of "whitespace" differs from .NET's.</summary>
internal static class JavaText
{
    /// <summary>Java's <c>Character.isWhitespace</c>: Unicode separators except the non-breaking ones, and the controls \t \n \x0B \f \r \x1C-\x1F.</summary>
    public static bool IsWhitespace(char c)
    {
        if (c is ' ' or ' ' or ' ') return false;
        return c is >= '\t' and <= '\r' or >= '\u001C' and <= '\u001F'
            || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.SpaceSeparator
                or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;
    }

    /// <summary><c>String.strip()</c>.</summary>
    public static string Strip(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && IsWhitespace(s[start])) start++;
        while (end > start && IsWhitespace(s[end - 1])) end--;
        return s[start..end];
    }
}
