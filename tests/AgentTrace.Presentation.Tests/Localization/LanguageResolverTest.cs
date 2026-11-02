using System.Globalization;
using AgentTrace.Presentation.Localization;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Localization;

public sealed class LanguageResolverTest
{
    private static CultureInfo Os(string name) => CultureInfo.GetCultureInfo(name);

    [Fact]
    public void TheArgumentWinsOverEverything()
    {
        Assert.Equal("es", LanguageResolver.Resolve("es", "en", Os("en-US")).Code);
        Assert.Equal("en", LanguageResolver.Resolve("en", "es", Os("es-ES")).Code);
    }

    [Fact]
    public void ThePreferenceWinsOverTheOperatingSystem()
    {
        Assert.Equal("es", LanguageResolver.Resolve(null, "es", Os("en-US")).Code);
        Assert.Equal("en", LanguageResolver.Resolve(null, "en", Os("es-ES")).Code);
    }

    [Fact]
    public void TheOperatingSystemLanguageIsUsedWhenSupported()
    {
        Assert.Equal("es", LanguageResolver.Resolve(null, null, Os("es-MX")).Code);
        Assert.Equal("en", LanguageResolver.Resolve(null, null, Os("en-GB")).Code);
    }

    [Fact]
    public void EnglishIsTheFallback()
    {
        Assert.Equal("en", LanguageResolver.Resolve(null, null, Os("fr-FR")).Code);
        Assert.Equal("en", LanguageResolver.Resolve(null, null, CultureInfo.InvariantCulture).Code);
    }

    [Fact]
    public void UnsupportedValuesAreSkippedNotFatal()
    {
        Assert.Equal("es", LanguageResolver.Resolve("fr", "es", Os("en-US")).Code);
        Assert.Equal("en", LanguageResolver.Resolve("fr", "de", Os("ja-JP")).Code);
        Assert.Equal("es", LanguageResolver.Resolve("", "  ", Os("es-ES")).Code);
    }

    [Fact]
    public void RegionAndCaseDoNotMatter()
    {
        Assert.Equal("es", LanguageResolver.Resolve("ES-mx", null, Os("en-US")).Code);
        Assert.Equal("en", LanguageResolver.Resolve("EN_us", null, Os("es-ES")).Code);
    }

    [Fact]
    public void NativeNamesAreNotTranslated()
    {
        Assert.Equal(["English", "Español"], SupportedLanguages.All.Select(l => l.NativeName)); // lang-ok
    }

    [Fact]
    public void FormattingKeepsTheRegionOfTheOperatingSystemWhenTheLanguageMatches()
    {
        Assert.Equal("es-MX", LanguageResolver.FormattingCulture(SupportedLanguages.Spanish, Os("es-MX")).Name);
        Assert.Equal("en-GB", LanguageResolver.FormattingCulture(SupportedLanguages.English, Os("en-GB")).Name);
    }

    [Fact]
    public void FormattingUsesTheLanguageDefaultWhenTheOperatingSystemDiffers()
    {
        Assert.Equal("es-ES", LanguageResolver.FormattingCulture(SupportedLanguages.Spanish, Os("en-US")).Name);
        Assert.Equal("en-US", LanguageResolver.FormattingCulture(SupportedLanguages.English, Os("fr-FR")).Name);
    }

    [Fact]
    public void ApplySetsTheDefaultAndTheCurrentCultures()
    {
        CultureInfo? defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        CultureInfo? defaultFormats = CultureInfo.DefaultThreadCurrentCulture;
        using var restore = CultureScope.English(); // restores the current thread's cultures
        try
        {
            LanguageResolver.Apply(SupportedLanguages.Spanish, Os("en-US"));
            Assert.Equal("es", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("es-ES", CultureInfo.CurrentCulture.Name);
            Assert.Equal("es", CultureInfo.DefaultThreadCurrentUICulture!.Name);
            Assert.Equal("es-ES", CultureInfo.DefaultThreadCurrentCulture!.Name);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = defaultUi;
            CultureInfo.DefaultThreadCurrentCulture = defaultFormats;
        }
    }
}
