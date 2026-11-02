namespace AgentTrace.Presentation.Localization;

/// <summary>A language the UI is translated to. <c>NativeName</c> is how the language names itself and is never translated.</summary>
public sealed record Language(string Code, string NativeName);

/// <summary>
/// The UI languages. English is the neutral resource set and the fallback; to add a language, add its
/// <c>X.&lt;code&gt;.resx</c> files and one entry here.
/// </summary>
public static class SupportedLanguages
{
    public static readonly Language English = new("en", "English");

    public static readonly Language Spanish = new("es", "Español"); // lang-ok: native name

    public static IReadOnlyList<Language> All { get; } = [English, Spanish];

    /// <summary>
    /// The supported language for a code or culture name ("es", "es-MX", "EN_us"): only the language part counts.
    /// Null when it is not supported (or empty).
    /// </summary>
    public static Language? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string language = code.Trim().Split('-', '_')[0];
        return All.FirstOrDefault(l => l.Code.Equals(language, StringComparison.OrdinalIgnoreCase));
    }
}
