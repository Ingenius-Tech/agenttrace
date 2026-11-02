using AgentTrace.Presentation.Localization;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Localization;

public sealed class LanguagePreferenceTest : TempDirTestBase
{
    [Fact]
    public void WithoutAFileThereIsNoPreference()
    {
        Assert.Null(new LanguagePreference(Dir.Combine("none", "language.txt")).Load());
    }

    [Fact]
    public void SavesAndLoadsTheCodeAcrossInstances()
    {
        var file = Dir.Combine("prefs", "language.txt");
        new LanguagePreference(file).Save(SupportedLanguages.Spanish);
        Assert.Equal("es", new LanguagePreference(file).Load());
        new LanguagePreference(file).Save(SupportedLanguages.English);
        Assert.Equal("en", new LanguagePreference(file).Load());
        Assert.Equal("en", File.ReadAllText(file.Value).Trim());
    }

    [Fact]
    public void AnUnsupportedOrGarbageValueMeansNoPreference()
    {
        var file = Dir.Combine("language.txt");
        File.WriteAllText(file.Value, "fr\n");
        Assert.Null(new LanguagePreference(file).Load());
        File.WriteAllText(file.Value, "\0\0 ??\n");
        Assert.Null(new LanguagePreference(file).Load());
        File.WriteAllText(file.Value, "");
        Assert.Null(new LanguagePreference(file).Load());
    }

    [Fact]
    public void ARegionCodeIsReducedToTheLanguage()
    {
        var file = Dir.Combine("language.txt");
        File.WriteAllText(file.Value, "\n  es-MX \n");
        Assert.Equal("es", new LanguagePreference(file).Load());
    }

    [Fact]
    public void AnUnwritableLocationDoesNotThrow()
    {
        var blocker = Dir.Combine("blocker");
        File.WriteAllText(blocker.Value, "x");
        var preference = new LanguagePreference(blocker.Combine("inside", "language.txt")); // the parent is a file
        preference.Save(SupportedLanguages.Spanish);
        Assert.Null(preference.Load());
    }
}
