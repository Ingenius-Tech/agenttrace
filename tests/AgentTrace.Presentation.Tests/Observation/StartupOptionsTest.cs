using AgentTrace.Core;
using AgentTrace.Presentation.Observation;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Observation;

public sealed class StartupOptionsTest : TempDirTestBase
{
    [Fact]
    public void NoArgumentsMeansNothingRequested()
    {
        StartupOptions o = StartupOptions.Parse([]);
        Assert.Null(o.Project);
        Assert.Null(o.Open);
        Assert.Null(o.Tab);
        Assert.Null(o.Capture);
        Assert.False(o.SoftwareRendering);
        Assert.Equal(StartupOptions.DefaultCaptureDelayMillis, o.CaptureDelayMillis);
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void TheFirstUnnamedArgumentIsTheProjectIfItIsAFolder()
    {
        Assert.Equal(Dir, StartupOptions.Parse([Dir.Value]).Project);
        Assert.Null(StartupOptions.Parse([Dir.Combine("does-not-exist").Value]).Project);
    }

    [Fact]
    public void NamedOptionsAreRead()
    {
        StartupOptions o = StartupOptions.Parse([
            Dir.Value, "--open=a.txt", "--diff=b.txt", "--tab=Knowledge", "--kg-select=c.txt", "--graph-select=last-error",
            "--graph-collapse=old", "--graph-orientation=vertical", "--graph-filter=errors,reads", "--graph-zoom=0.5", "--kg-view=3d", "--kg-yaw=-45", "--kg-pitch=20.5", "--search=mvn error",
            "--heatmap-zoom=src", "--heatmap-filter=errors", "--heatmap-area=Activity", "--heatmap-select=src/a.cs",
            "--capture=out.png", "--capture-delay=250", "--capture-mode=printwindow", "--render=software"]);
        Assert.Equal("a.txt", o.Open); // the relative path is understood from the project, not from where it is started
        Assert.Equal("b.txt", o.Diff);
        Assert.Equal("knowledge", o.Tab);
        Assert.Equal("c.txt", o.KnowledgeSelect);
        Assert.Equal("last-error", o.GraphSelect);
        Assert.Equal("old", o.GraphCollapse);
        Assert.Equal("vertical", o.GraphOrientation);
        Assert.Equal("errors,reads", o.GraphFilter);
        Assert.Equal(0.5, o.GraphZoom);
        Assert.Equal("3d", o.KnowledgeView);
        Assert.Equal(-45, o.KnowledgeYaw);
        Assert.Equal(20.5, o.KnowledgePitch);
        Assert.Equal("mvn error", o.Search);
        Assert.Equal("src", o.HeatmapZoom);
        Assert.Equal("errors", o.HeatmapFilter);
        Assert.Equal("activity", o.HeatmapArea);
        Assert.Equal("src/a.cs", o.HeatmapSelect);
        Assert.Equal("out.png", o.Capture);
        Assert.Equal(250, o.CaptureDelayMillis);
        Assert.True(o.SoftwareRendering);
        Assert.True(o.PrintWindowCapture);
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void WhatIsNotUnderstoodIsReportedAndDoesNotStopTheStartup()
    {
        StartupOptions o = StartupOptions.Parse(["--tab=other", "--graph-zoom=much", "--capture-delay=-1", "--render=gpu", "--kg-view=4d", "--kg-yaw=x", "--heatmap-area=square"]);
        Assert.Null(o.Tab);
        Assert.Null(o.GraphZoom);
        Assert.Equal(StartupOptions.DefaultCaptureDelayMillis, o.CaptureDelayMillis);
        Assert.False(o.SoftwareRendering);
        Assert.Null(o.KnowledgeView);
        Assert.Null(o.KnowledgeYaw);
        Assert.Null(o.HeatmapArea);
        Assert.Equal(
            [
                "Unknown --tab: other", "--graph-zoom is not a number: much", "Unknown --kg-view: 4d", "--kg-yaw is not a number: x",
                "Unknown --heatmap-area: square", "--capture-delay is not a number: -1", "Unknown --render: gpu",
            ],
            o.Warnings);
    }

    [Fact]
    public void TheWarningsAreSpanishUnderEs()
    {
        using var _ = CultureScope.Spanish();
        StartupOptions o = StartupOptions.Parse(["--tab=other", "--graph-zoom=much", "--render=gpu"]);
        Assert.Equal(["--tab desconocida: other", "--graph-zoom no es un número: much", "--render desconocido: gpu"], o.Warnings); // lang-ok
    }

    [Fact]
    public void TheHeatmapTabIsKnown()
    {
        Assert.Equal("heatmap", StartupOptions.Parse(["--tab=HeatMap"]).Tab);
        StartupOptions none = StartupOptions.Parse([]);
        Assert.Null(none.HeatmapZoom);
        Assert.Null(none.HeatmapFilter);
        Assert.Null(none.HeatmapArea);
        Assert.Null(none.HeatmapSelect);
    }

    [Theory]
    [InlineData("--lang=en", "en")]
    [InlineData("--lang=es", "es")]
    [InlineData("--lang=ES", "es")]
    [InlineData("--lang=es-MX", "es")]
    public void LangIsReducedToASupportedLanguageCode(string arg, string expected)
    {
        StartupOptions o = StartupOptions.Parse([arg]);
        Assert.Equal(expected, o.Lang);
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void NoLangMeansNoRequest()
    {
        Assert.Null(StartupOptions.Parse([]).Lang);
        Assert.Null(StartupOptions.Parse(["--lang="]).Lang);
        Assert.Empty(StartupOptions.Parse(["--lang="]).Warnings);
    }

    [Fact]
    public void AnUnknownLangWarnsInTheCurrentLanguageAndIsIgnored()
    {
        StartupOptions o = StartupOptions.Parse(["--lang=fr"]);
        Assert.Null(o.Lang);
        Assert.Equal(["--lang is not supported: fr (supported: en, es)"], o.Warnings);
        using var _ = CultureScope.Spanish();
        Assert.Equal(["--lang no admitido: fr (admitidos: en, es)"], StartupOptions.Parse(["--lang=fr"]).Warnings); // lang-ok
    }

    [Fact]
    public void BlankValuesCountAsAbsent()
    {
        StartupOptions o = StartupOptions.Parse(["--open=", "--kg-select=  "]);
        Assert.Null(o.Open);
        Assert.Null(o.KnowledgeSelect);
    }
}
