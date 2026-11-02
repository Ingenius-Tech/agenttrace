using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using AgentTrace.Adapters.ClaudeCode;
using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Desktop.Controls;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Graph;
using AgentTrace.Testing;

namespace AgentTrace.Desktop.Tests;

/// <summary>The execution graph with the synthetic Claude Code trace, in a headless window with real Skia.</summary>
public sealed class GraphPaneTests
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    private static FlowGraph SyntheticGraph()
    {
        FilePath session = new FilePath(AppContext.BaseDirectory).Combine("TestData", "claude", "projects", "D--fake-proj", Session + ".jsonl");
        var builder = new FlowBuilder();
        foreach (AgentEvent e in SessionReader.ReadSession(new ClaudeCodeAdapter(), session)) builder.Accept(e);
        return builder.Snapshot();
    }

    private static string CapturesDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public Task TheSyntheticSessionIsDrawnAndItsLastErrorShowsItsDetail() => HeadlessHost.Run(() =>
    {
        var pane = new GraphPane(_ => { }, _ => { });
        var window = new Window { Width = 1200, Height = 700, Content = pane };
        window.Show();
        pane.SetGraph(SyntheticGraph());
        Assert.True(pane.Canvas.Graph.StepCount > 0);
        pane.Canvas.Select(GraphSelection.Parse("last-error", pane.Canvas.Graph));
        Assert.True(pane.DetailVisible);
        Assert.Contains(pane.Detail.Texts, t => t.Contains("error", StringComparison.OrdinalIgnoreCase));
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(CapturesDir(), "graph-vertical.png"), new PngBitmapEncoderOptions());

        pane.ShowOrientation(GraphOrientation.Horizontal);
        using WriteableBitmap? horizontal = window.CaptureRenderedFrame();
        horizontal!.Save(Path.Combine(CapturesDir(), "graph-horizontal.png"), new PngBitmapEncoderOptions());
    });

    [Fact]
    public Task TheSummaryBarIsEnglishByDefaultAndSpanishWhenTheCultureSaysSo() => HeadlessHost.Run(() =>
    {
        var pane = new GraphPane(_ => { }, _ => { });
        pane.SetGraph(SyntheticGraph());
        Assert.Contains(" steps · ", pane.Summary, StringComparison.Ordinal);
        using var _ = CultureScope.Spanish();
        var spanish = new GraphPane(_ => { }, _ => { });
        spanish.SetGraph(SyntheticGraph());
        Assert.Contains(" pasos · ", spanish.Summary, StringComparison.Ordinal); // lang-ok
    });
}
