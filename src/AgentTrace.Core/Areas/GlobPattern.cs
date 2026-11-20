using System.Text;
using System.Text.RegularExpressions;

namespace AgentTrace.Core.Areas;

/// <summary>Why a glob pattern is rejected. Rejected patterns are never silently reinterpreted.</summary>
public enum GlobError
{
    Empty,
    EmptySegment,
    Backslash,
    Braces,
    CharacterClass,
    Negation,
    Absolute,
    DotSegment,
    TripleStar,
    MixedDoubleStar,
}

/// <summary>
/// A glob pattern over repository-relative, <c>/</c>-separated paths, compared ordinally and case-sensitively on every OS.
/// The contract (documented in <c>docs/AREAS.md</c> and covered by a test that runs that table):
/// <c>*</c> is any characters except <c>/</c>, <c>?</c> is one character except <c>/</c>, and <c>**</c> as a whole segment is zero or
/// more segments. A pattern without <c>/</c> (apart from a trailing one) matches at any depth; one with a leading or inner <c>/</c>
/// is anchored at the root. A pattern matches a path if it matches the path or any ancestor folder (gitignore semantics); a trailing
/// <c>/</c> restricts the match to folders, so it needs something below it.
/// </summary>
public sealed class GlobPattern
{
    private readonly Regex _regex;

    private GlobPattern(string source, Regex regex)
    {
        Source = source;
        _regex = regex;
    }

    /// <summary>The pattern as written.</summary>
    public string Source { get; }

    /// <summary>The error of a pattern, or null if it is valid.</summary>
    public static GlobError? Validate(string pattern) => Parse(pattern, out _);

    /// <summary>Compiles a pattern; returns null and the error if it is rejected.</summary>
    public static GlobPattern? TryCreate(string pattern, out GlobError? error)
    {
        error = Parse(pattern, out GlobPattern? glob);
        return glob;
    }

    /// <summary>True if <paramref name="relativePath"/> (<c>/</c>-separated, no leading <c>/</c>) is matched.</summary>
    public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath);

    private static GlobError? Parse(string pattern, out GlobPattern? glob)
    {
        glob = null;
        if (pattern.Length == 0) return GlobError.Empty;
        if (pattern.Contains('\\')) return GlobError.Backslash;
        if (pattern.Contains('{') || pattern.Contains('}')) return GlobError.Braces;
        if (pattern.Contains('[') || pattern.Contains(']')) return GlobError.CharacterClass;
        if (pattern[0] == '!') return GlobError.Negation;
        if (pattern.StartsWith("//", StringComparison.Ordinal)) return GlobError.Absolute;
        if (pattern.Length >= 2 && char.IsAsciiLetter(pattern[0]) && pattern[1] == ':') return GlobError.Absolute;

        string body = pattern;
        bool rooted = body.StartsWith('/');
        if (rooted) body = body[1..];
        bool foldersOnly = body.EndsWith('/');
        if (foldersOnly) body = body[..^1];
        if (body.Length == 0) return GlobError.Empty;

        string[] segments = body.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0) return GlobError.EmptySegment;
            if (segment is "." or "..") return GlobError.DotSegment;
            if (segment.Contains("***", StringComparison.Ordinal)) return GlobError.TripleStar;
            if (segment != "**" && segment.Contains("**", StringComparison.Ordinal)) return GlobError.MixedDoubleStar;
        }

        bool anchored = rooted || segments.Length > 1;
        var regex = new StringBuilder(anchored ? "^" : "^(?:.*/)?");
        for (int i = 0; i < segments.Length; i++)
        {
            bool last = i == segments.Length - 1;
            if (segments[i] == "**")
            {
                regex.Append(last ? ".*" : "(?:[^/]+/)*");
                continue;
            }
            foreach (char c in segments[i])
            {
                regex.Append(c switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(c.ToString()) });
            }
            if (!last) regex.Append('/');
        }
        regex.Append(foldersOnly ? "/.*$" : "(?:/.*)?$");
        glob = new GlobPattern(pattern, new Regex(regex.ToString(),
            RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Singleline));
        return null;
    }
}
