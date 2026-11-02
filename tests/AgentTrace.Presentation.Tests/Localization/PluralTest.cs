using AgentTrace.Presentation.Localization;

namespace AgentTrace.Presentation.Tests.Localization;

public sealed class PluralTest
{
    [Theory]
    [InlineData(0, "other")]
    [InlineData(1, "one")]
    [InlineData(2, "other")]
    [InlineData(1000, "other")]
    [InlineData(-1, "other")]
    public void OnlyExactlyOneIsSingular(long n, string expected) => Assert.Equal(expected, Plural.Pick(n, "one", "other"));
}
