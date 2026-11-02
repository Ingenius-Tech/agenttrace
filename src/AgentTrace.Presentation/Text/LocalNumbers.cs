using System.Globalization;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// Numbers with the separators of <see cref="CultureInfo.CurrentCulture"/> (en: <c>4.9</c>, <c>1,234</c>; es: <c>4,9</c>,
/// <c>1.234</c>) and integer arithmetic: rounding does not depend on double formatting and goes half up, like the
/// <c>%.1f</c> of Java over an exact decimal.
/// </summary>
internal static class LocalNumbers
{
    /// <summary><c>value / unit</c> with one decimal: in English <c>OneDecimal(4932, 1000)</c> is "4.9", in Spanish "4,9".</summary>
    public static string OneDecimal(long value, long unit)
    {
        if (value < 0) return "-" + OneDecimal(-value, unit);
        long tenths = (value + unit / 20) / (unit / 10);
        return (tenths / 10).ToString(CultureInfo.InvariantCulture)
            + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator
            + (tenths % 10).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>With the group separator: 1234567 is "1,234,567" in English and "1.234.567" in Spanish.</summary>
    public static string Grouped(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>A decimal with one digit, rounded half up (not .NET's round-half-even), with the culture's decimal separator.</summary>
    public static string OneDecimal(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.CurrentCulture);
}
