using System.Globalization;

namespace AgentTrace.Presentation.Localization;

/// <summary>Chooses the UI language and the cultures to apply. Pure logic: it reads nothing from the machine.</summary>
public static class LanguageResolver
{
    /// <summary>
    /// <c>--lang</c> argument, then the saved preference, then the OS UI language if it is supported, then English.
    /// An argument or preference that is not supported is skipped (the unknown <c>--lang</c> warning is the caller's).
    /// </summary>
    public static Language Resolve(string? arg, string? saved, CultureInfo os) =>
        SupportedLanguages.Find(arg)
        ?? SupportedLanguages.Find(saved)
        ?? SupportedLanguages.Find(os.Name)
        ?? SupportedLanguages.English;

    /// <summary>
    /// The culture for formatting numbers and dates: the OS regional culture when it has the same language (es-MX,
    /// en-GB keep their conventions), else the language's default culture.
    /// </summary>
    public static CultureInfo FormattingCulture(Language language, CultureInfo osFormats) =>
        osFormats.TwoLetterISOLanguageName == language.Code ? osFormats : CultureInfo.CreateSpecificCulture(language.Code);

    /// <summary>Sets the default and the current thread cultures; call it once before the UI starts.</summary>
    public static void Apply(Language language, CultureInfo osFormats)
    {
        var ui = CultureInfo.GetCultureInfo(language.Code);
        CultureInfo formats = FormattingCulture(language, osFormats);
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        CultureInfo.DefaultThreadCurrentCulture = formats;
        CultureInfo.CurrentUICulture = ui;
        CultureInfo.CurrentCulture = formats;
    }
}
