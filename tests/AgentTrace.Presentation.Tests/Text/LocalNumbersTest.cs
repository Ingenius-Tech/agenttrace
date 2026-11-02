using AgentTrace.Presentation.Text;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class LocalNumbersTest
{
    [Theory]
    [InlineData("en-US", 4_932, 1_000, "4.9")]
    [InlineData("es-ES", 4_932, 1_000, "4,9")]
    [InlineData("en-US", 1_050, 1_000, "1.1")] // half rounds up
    [InlineData("es-ES", 1_050, 1_000, "1,1")]
    [InlineData("en-US", 1_049, 1_000, "1.0")]
    [InlineData("en-US", 59_950, 1_000, "60.0")]
    [InlineData("es-ES", 999_950, 1_000, "1000,0")]
    [InlineData("en-US", 1_150_000, 1_000_000, "1.2")]
    [InlineData("en-US", -4_932, 1_000, "-4.9")]
    [InlineData("es-ES", 0, 1_000, "0,0")]
    public void OneDecimalUsesTheDecimalSeparatorOfTheCulture(string culture, long value, long unit, string expected)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal(expected, LocalNumbers.OneDecimal(value, unit));
    }

    [Theory]
    [InlineData("en-US", 0, "0")]
    [InlineData("en-US", 999, "999")]
    [InlineData("en-US", 1_234, "1,234")]
    [InlineData("en-US", 1_234_567, "1,234,567")]
    [InlineData("es-ES", 1_234, "1.234")]
    [InlineData("es-ES", 1_234_567, "1.234.567")]
    public void GroupedUsesTheGroupSeparatorOfTheCulture(string culture, long value, string expected)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal(expected, LocalNumbers.Grouped(value));
    }

    [Theory]
    [InlineData("en-US", "1.5", "0.3")]
    [InlineData("es-ES", "1,5", "0,3")]
    public void DecimalValuesRoundHalfUpWithTheCultureSeparator(string culture, string expectedOneAndAHalf, string expectedQuarter)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal(expectedOneAndAHalf, LocalNumbers.OneDecimal(1536 / 1024m));
        Assert.Equal(expectedQuarter, LocalNumbers.OneDecimal(0.25m)); // not the banker's rounding of .NET
    }
}
