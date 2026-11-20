using System.Globalization;
using AgentTrace.Core.Areas;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Presentation.Areas;

/// <summary>Turns the errors of a rules file (data) into localized text, one line per error. No Avalonia.</summary>
public static class AreaErrorText
{
    public static string Of(AreaRuleError error)
    {
        string message = Message(error);
        if (error.AreaIndex is not { } index) return message;
        string area = error.AreaId ?? string.Format(CultureInfo.CurrentCulture, AreaStrings.ErrorAreaPositionFormat, LocalNumbers.Grouped(index + 1));
        return string.Format(CultureInfo.CurrentCulture, AreaStrings.ErrorAreaFormat, area, message);
    }

    private static string Message(AreaRuleError e)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        return e.Kind switch
        {
            AreaRuleErrorKind.Unreadable => AreaStrings.ErrorUnreadable,
            AreaRuleErrorKind.InvalidJson => e.Line is { } line ? string.Format(culture, AreaStrings.ErrorInvalidJsonFormat, LocalNumbers.Grouped(line)) : AreaStrings.ErrorInvalidJson,
            AreaRuleErrorKind.UnsupportedVersion => string.Format(culture, AreaStrings.ErrorUnsupportedVersionFormat, e.Field),
            AreaRuleErrorKind.MissingField => string.Format(culture, AreaStrings.ErrorMissingFieldFormat, e.Field),
            AreaRuleErrorKind.UnknownDimension => string.Format(culture, AreaStrings.ErrorUnknownDimensionFormat, e.Field),
            AreaRuleErrorKind.InvalidId => AreaStrings.ErrorInvalidId,
            AreaRuleErrorKind.DuplicateId => AreaStrings.ErrorDuplicateId,
            AreaRuleErrorKind.EmptyInclude => AreaStrings.ErrorEmptyInclude,
            AreaRuleErrorKind.InvalidGlob => string.Format(culture, AreaStrings.ErrorInvalidGlobFormat, e.Pattern, e.GlobError is { } glob ? Glob(glob) : ""),
            _ => throw new ArgumentOutOfRangeException(nameof(e)),
        };
    }

    public static string Glob(GlobError error) => error switch
    {
        GlobError.Empty => AreaStrings.GlobEmpty,
        GlobError.EmptySegment => AreaStrings.GlobEmptySegment,
        GlobError.Backslash => AreaStrings.GlobBackslash,
        GlobError.Braces => AreaStrings.GlobBraces,
        GlobError.CharacterClass => AreaStrings.GlobCharacterClass,
        GlobError.Negation => AreaStrings.GlobNegation,
        GlobError.Absolute => AreaStrings.GlobAbsolute,
        GlobError.DotSegment => AreaStrings.GlobDotSegment,
        GlobError.TripleStar => AreaStrings.GlobTripleStar,
        GlobError.MixedDoubleStar => AreaStrings.GlobMixedDoubleStar,
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };
}
