using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Desktop.Controls;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Desktop.Tests;

/// <summary>The knowledge graph in 2D and 3D with a synthetic session of 12 turns and about 70 files, in a headless window with real Skia.</summary>
public sealed class KnowledgePaneTests
{
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\project" : "/project");

    private static EventHeader Hd() => new(new SessionId("s"), "test", null, null, new SourceRef(new FilePath("t"), 1));

    private static KnowledgeGraph Session()
    {
        var b = new KnowledgeBuilder(Root);
        string[] modules = ["core", "ui", "git", "ingest"];
        for (int turn = 0; turn < 12; turn++)
        {
            b.Accept(new UserMessage(Hd(), "p"));
            string module = modules[turn % modules.Length];
            for (int i = 0; i < 6; i++)
            {
                FilePath f = Root.Combine(module).Combine("f" + (turn * 6 + i) + ".cs");
                if (i < 2) b.Accept(new FileRead(Hd(), f, "r"));
                else b.Accept(new FileChanged(Hd(), f, i == 2 ? ChangeKind.Created : ChangeKind.Modified, "w"));
            }
        }
        return b.Snapshot();
    }

    private static string CapturesDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Save(Window window, string name)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(CapturesDir(), name + ".png"), new PngBitmapEncoderOptions());
    }

    [Fact]
    public Task The3DViewDrawsTheTimeAxisAndSelectsByProjection() => HeadlessHost.Run(() =>
    {
        var pane = new KnowledgePane(_ => { }, _ => { });
        var window = new Window { Width = 1200, Height = 700, Content = pane };
        window.Show();
        pane.SetProjectRoot(Root);
        pane.SetGraph(Session());
        Assert.False(pane.Canvas.Mode3D);
        Save(window, "knowledge-2d");

        pane.ShowView("3d");
        Assert.True(pane.Canvas.Mode3D);
        Save(window, "knowledge-3d");
        pane.ShowView("side");
        Save(window, "knowledge-side");
        pane.ShowView("top");
        Save(window, "knowledge-top");

        // With the free view, a file is selected and the camera centers on it without non-finite values
        pane.ShowView("3d");
        FilePath first = pane.Canvas.Graph.Nodes[0].Path;
        pane.Canvas.Select(first);
        Assert.Equal(first, pane.Canvas.SelectedPath);
        Assert.True(double.IsFinite(pane.Canvas.Camera.TargetX) && double.IsFinite(pane.Canvas.Camera.TargetZ));
        Assert.True(pane.DetailVisible);

        pane.ShowView("2d");
        Assert.False(pane.Canvas.Mode3D);
    });

    [Fact]
    public Task TheSummaryIsWordedInTheCurrentCulture() => HeadlessHost.Run(() =>
    {
        var pane = new KnowledgePane(_ => { }, _ => { });
        Assert.Equal("No files yet", pane.Summary);
        pane.SetGraph(Session());
        Assert.Matches(@"^\d+ files · \d+ relations · 12 turns$", pane.Summary);

        using var scope = CultureScope.Spanish();
        var spanish = new KnowledgePane(_ => { }, _ => { });
        Assert.Equal("Sin archivos todavía", spanish.Summary); // lang-ok
        spanish.SetGraph(Session());
        Assert.Matches(@"^\d+ archivos · \d+ relaciones · 12 turnos$", spanish.Summary); // lang-ok
    });
}
