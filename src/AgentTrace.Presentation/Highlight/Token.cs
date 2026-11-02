namespace AgentTrace.Presentation.Highlight;

/// <summary>A fragment of a line: from <c>Start</c> (inclusive) to <c>End</c> (exclusive).</summary>
public readonly record struct Token(int Start, int End, TokenType Type);

/// <summary>Class of a highlighted text fragment. What has no class is shown with the normal color.</summary>
public enum TokenType { Keyword, String, Comment, Number }

public static class TokenTypes
{
    /// <summary>Name of the style class it is painted with (<c>syntax-keyword</c>…).</summary>
    public static string StyleClass(this TokenType type) => "syntax-" + type.ToString().ToLowerInvariant();
}
