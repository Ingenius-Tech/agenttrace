using AgentTrace.Core;
using AgentTrace.Presentation.Observation;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Observation;

public sealed class PanoramaPreferenceTest : TempDirTestBase
{
    private FilePath File1 => Dir.Combine("panorama.txt");

    [Fact]
    public void ALayoutSurvivesTheRoundTripAndANewInstance()
    {
        FilePath file = Dir.Combine("prefs", "panorama.txt"); // the folder does not exist yet
        var layout = new PanoramaLayout(false, true, true, 0.55, 0.25);
        new PanoramaPreference(file).Save(layout);
        Assert.Equal(layout, new PanoramaPreference(file).Load());
    }

    [Fact]
    public void AMissingFileMeansTheDefaultLayout()
    {
        Assert.Equal(PanoramaLayout.Default, new PanoramaPreference(Dir.Combine("nothing", "panorama.txt")).Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage without equals")]
    [InlineData("graph=maybe\ncolumn=wide\nrow=")]
    [InlineData("\0\0\0=\0")]
    public void ACorruptFileMeansTheDefaultLayout(string content)
    {
        File.WriteAllText(File1.Value, content);
        Assert.Equal(PanoramaLayout.Default, new PanoramaPreference(File1).Load());
    }

    [Fact]
    public void ABinaryFileDoesNotThrow()
    {
        File.WriteAllBytes(File1.Value, [0xFF, 0xFE, 0x00, 0x80, 0xC3]);
        Assert.Equal(PanoramaLayout.Default, new PanoramaPreference(File1).Load());
    }

    [Fact]
    public void UnknownKeysAndPaneNamesAreIgnoredAndWhatIsValidIsKept()
    {
        File.WriteAllText(File1.Value, "heatmap=false\nfuture=1\nGRAPH=false\nknowledge=true\nareas=true\ncolumn=0.5\n");
        Assert.Equal(new PanoramaLayout(false, true, true, 0.5, PanoramaLayout.Default.RowRatio), new PanoramaPreference(File1).Load());
    }

    [Fact]
    public void RatiosOutOfRangeAreClamped()
    {
        File.WriteAllText(File1.Value, "column=0.01\nrow=7\n");
        PanoramaLayout layout = new PanoramaPreference(File1).Load();
        Assert.Equal(PanoramaLayout.MinRatio, layout.ColumnRatio);
        Assert.Equal(PanoramaLayout.MaxRatio, layout.RowRatio);
        File.WriteAllText(File1.Value, "column=NaN\nrow=-Infinity\n");
        Assert.Equal(PanoramaLayout.Default, new PanoramaPreference(File1).Load());
    }

    [Fact]
    public void WithNoPaneShownEveryPaneIsShown()
    {
        File.WriteAllText(File1.Value, "graph=false\nareas=false\nknowledge=false\n");
        Assert.Equal(PanoramaLayout.Default, new PanoramaPreference(File1).Load());
    }

    [Fact]
    public void TheFileIsInvariantWhateverTheCulture()
    {
        using var _ = new CultureScope("es");
        new PanoramaPreference(File1).Save(PanoramaLayout.Default with { ColumnRatio = 0.45 });
        Assert.Contains("column=0.45", File.ReadAllText(File1.Value), StringComparison.Ordinal);
        Assert.Equal(0.45, new PanoramaPreference(File1).Load().ColumnRatio);
    }

    [Fact]
    public void SavingWhereItCannotBeWrittenDoesNotThrow()
    {
        FilePath blocker = Dir.Combine("blocker");
        File.WriteAllText(blocker.Value, "a file where a folder is needed");
        var preference = new PanoramaPreference(blocker.Combine("panorama.txt"));
        preference.Save(PanoramaLayout.Default);
        Assert.Equal(PanoramaLayout.Default, preference.Load());
    }

    [Fact]
    public void WithPanesKeepsTheNamedOnesAndIgnoresTheUnknown()
    {
        Assert.Equal(new PanoramaLayout(true, true, false, 0.38, 0.40), PanoramaLayout.Default.WithPanes(["Graph", "areas", "heatmap"]));
        Assert.Equal(PanoramaLayout.Default, PanoramaLayout.Default.WithPanes(["heatmap"]));
    }
}
