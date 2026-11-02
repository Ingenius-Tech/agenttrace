using System.Text;
using System.Text.RegularExpressions;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// The two Java text operations the formatters use and that behave differently in C#:
/// <c>replaceAll("\\s+", " ")</c> (ASCII spaces only) and <c>strip()</c> (<c>Character.isWhitespace</c>, which does not count
/// non-breaking spaces or U+0085, and does count the separators U+001C to U+001F).
/// </summary>
internal static partial class JavaText
{
    [GeneratedRegex("[ \\t\\n\\x0B\\f\\r]+")]
    private static partial Regex AsciiWhitespace();

    public static string CollapseWhitespace(string text) => AsciiWhitespace().Replace(text, " ");

    /// <summary>Java's <c>Character.isWhitespace</c>.</summary>
    public static bool IsWhitespace(char c) => c switch
    {
        ' ' or ' ' or ' ' or '\u0085' => false,
        >= '\u001C' and <= '\u001F' => true,
        _ => char.IsWhiteSpace(c),
    };

    /// <summary>Java's <c>String.strip()</c>.</summary>
    public static string Strip(string text)
    {
        int start = 0;
        int end = text.Length;
        while (start < end && IsWhitespace(text[start])) start++;
        while (end > start && IsWhitespace(text[end - 1])) end--;
        return text[start..end];
    }

    /// <summary>Flattens line breaks and repeated spaces and cuts to <paramref name="maxLength"/> with "…".</summary>
    public static string OneLine(string? text, int maxLength)
    {
        string flat = text is null ? "" : Strip(CollapseWhitespace(text));
        return flat.Length > maxLength ? flat[..maxLength] + "…" : flat;
    }
}
