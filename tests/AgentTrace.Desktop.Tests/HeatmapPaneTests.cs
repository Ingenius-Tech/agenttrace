using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Desktop.Controls;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Desktop.Tests;

/// <summary>The heatmap with a synthetic project of 70 files (7 folders of 10) and a session of 3 turns, in a headless window with real Skia.</summary>
public sealed class HeatmapPaneTests
{
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\project" : "/project");

    private static readonly string[] Folders = ["core", "ui", "git", "ingest", "tests", "docs", "tools"];

    private static FilePath File(string folder, int i) => Root.Combine(folder, "f" + i + ".cs");

    private static ProjectListing Listing() =>
        new([.. Folders.SelectMany((folder, d) => Enumerable.Range(0, 10).Select(i => new ListedFile(File(folder, i), 800 + ((d + 3) * 7919 + i * 1237) % 30_000, false)))], false);

    private static KnowledgeGraph.Node Node(FilePath path, int reads, int created, int modified, int errors, int lastTurn, ChangeKind? last) =>
        new(path, last, reads, created, modified, 0, 0, 1 + lastTurn, 0, lastTurn, errors);

    /// <summary>Reads in "core" and "ui", changes in "core" and "git", errors in "ui" and "tests"; about 25 touched files.</summary>
    private static KnowledgeGraph Session(bool targetsKnown = true)
    {
        List<KnowledgeGraph.Node> nodes = [];
        for (int i = 0; i < 10; i++) nodes.Add(Node(File("core", i), 1 + i % 4, i == 0 ? 1 : 0, i % 3 == 0 ? 2 : 0, 0, i % 3, i % 3 == 0 ? ChangeKind.Modified : null));
        for (int i = 0; i < 6; i++) nodes.Add(Node(File("ui", i), 2, 0, 0, i < 2 ? 3 : 0, 1, null));
        for (int i = 0; i < 5; i++) nodes.Add(Node(File("git", i), 0, 0, 1 + i % 2, 0, 2, ChangeKind.Modified));
        for (int i = 0; i < 3; i++) nodes.Add(Node(File("tests", i), 1, 0, 0, i == 0 ? 4 : 1, 2, null));
        nodes.Add(Node(File("docs", 0), 0, 1, 0, 0, 2, ChangeKind.Created));
        return new KnowledgeGraph(nodes.ToValueList(), ValueList<KnowledgeGraph.Edge>.Empty, 3, targetsKnown ? 2 : 5, targetsKnown);
    }

    private static HeatTree TreeOf(KnowledgeGraph graph) => HeatTree.Build(Root, Listing(), graph, FileStatusIndex.Empty(Root));

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

    private static (HeatmapPane Pane, Window Window, List<FilePath> Opened) Open(KnowledgeGraph graph)
    {
        List<FilePath> opened = [];
        var pane = new HeatmapPane(opened.Add, _ => { }, _ => { }, _ => { });
        var window = new Window { Width = 1200, Height = 700, Content = pane };
        window.Show();
        pane.SetProjectRoot(Root);
        pane.SetTree(TreeOf(graph), graph);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (pane, window, opened);
    }

    /// <summary>The center of the cell of a node, in window coordinates.</summary>
    private static Point CenterOf(HeatmapPane pane, Window window, FilePath path)
    {
        TreemapCell cell = pane.Canvas.Layout!.Cells.First(c => c.Node.Path == path);
        // A folder is pointed at by its header: the center of the cell falls on one of its children
        double y = cell.Node.Directory ? cell.Bounds.Y + 6 : cell.Bounds.Y + cell.Bounds.H / 2;
        return pane.Canvas.TranslatePoint(new Point(cell.Bounds.X + cell.Bounds.W / 2, y), window)!.Value;
    }

    private static void Click(Window window, Point p)
    {
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
    }

    [Fact]
    public Task TheMapDrawsTheProjectInFourStates() => HeadlessHost.Run(() =>
    {
        (HeatmapPane pane, Window window, _) = Open(Session());
        Assert.NotNull(pane.Canvas.Layout);
        Assert.True(pane.Canvas.Layout.Cells.Count >= 70, "at least one cell per file: " + pane.Canvas.Layout.Cells.Count);
        Assert.Equal(HeatArea.Size, pane.Canvas.Area);
        Assert.True(pane.ErrorsEnabled);
        Assert.Equal(["project"], pane.Crumbs);
        Assert.StartsWith("70 files, 25 touched", pane.Summary, StringComparison.Ordinal);
        Save(window, "heatmap-activity");

        // Errors only: what failed is painted red and the rest stays in the card color
        pane.SetFilter(new ActivityFilter(false, false, true));
        Assert.Equal(new ActivityFilter(false, false, true), pane.Canvas.Filter);
        Save(window, "heatmap-errors");

        // Zooming into a folder: the breadcrumbs name it and the map fills with it
        pane.SetFilter(ActivityFilter.All);
        pane.Canvas.ZoomTo(Root.Combine("core"));
        Assert.Equal(["project", "core"], pane.Crumbs);
        Assert.Equal(Root.Combine("core"), pane.Canvas.Layout!.Zoom.Path);
        Save(window, "heatmap-zoom");

        // With the area set to "Activity" what was not touched disappears
        pane.Canvas.ZoomUp();
        pane.SetArea(HeatArea.Activity);
        Assert.Equal(HeatArea.Activity, pane.Canvas.Area);
        Assert.DoesNotContain(pane.Canvas.Layout!.Cells, c => !c.Node.Directory && c.Node.Activity.Total == 0);
        Assert.Contains(pane.Canvas.Layout.Cells, c => c.Node.Path == File("core", 0));
        Save(window, "heatmap-area-activity");
        window.Close();
    });

    [Fact]
    public Task AClickSelectsAFileAndOpensItsDetailTheDoubleClickZoomsAndBackspaceGoesUp() => HeadlessHost.Run(() =>
    {
        (HeatmapPane pane, Window window, List<FilePath> opened) = Open(Session());
        FilePath touched = File("core", 0);
        FilePath untouched = File("tools", 5);
        Assert.False(pane.DetailVisible);

        // A click on a touched file selects it and opens the "Knowledge" detail
        Click(window, CenterOf(pane, window, touched));
        Assert.Equal(touched, pane.Canvas.SelectedPath);
        Assert.True(pane.DetailVisible);
        Assert.Contains(pane.DetailTexts, t => t.Contains("f0.cs", StringComparison.Ordinal) || t.Contains("core", StringComparison.Ordinal));
        Save(window, "heatmap-selected");

        // One without activity is selected but there is no detail
        Click(window, CenterOf(pane, window, untouched));
        Assert.Equal(untouched, pane.Canvas.SelectedPath);
        Assert.False(pane.DetailVisible);
        Assert.Empty(opened);

        // A double click on a folder zooms in; the breadcrumbs show it
        Point header = CenterOf(pane, window, Root.Combine("ui"));
        Click(window, header);
        Click(window, header);
        Assert.Equal(Root.Combine("ui"), pane.Canvas.Zoom);
        Assert.Equal(["project", "ui"], pane.Crumbs);
        Assert.Empty(opened);

        // Backspace goes up one level
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
        Assert.Equal(Root, pane.Canvas.Zoom);
        Assert.Equal(["project"], pane.Crumbs);

        // A double click on a file opens it
        Point file = CenterOf(pane, window, untouched);
        Click(window, file);
        Click(window, file);
        Assert.Equal(untouched, Assert.Single(opened));
        window.Close();
    });

    [Fact]
    public Task ErrorsAreDisabledWhenTheAgentDoesNotSayWhichFileFails() => HeatmapPaneErrors(targetsKnown: false);

    [Fact]
    public Task ErrorsAreEnabledWhenTheAgentSaysWhichFileFails() => HeatmapPaneErrors(targetsKnown: true);

    private static Task HeatmapPaneErrors(bool targetsKnown) => HeadlessHost.Run(() =>
    {
        (HeatmapPane pane, Window window, _) = Open(Session(targetsKnown));
        Assert.Equal(targetsKnown, pane.ErrorsEnabled);
        Assert.Equal(targetsKnown, pane.Canvas.Filter.Errors);
        Assert.Equal(targetsKnown ? "Count the calls that fail on the file" : HeatText.ErrorsUnavailable, pane.ErrorsTooltip);
        Assert.Contains(targetsKnown ? "2 errors without a file" : "5 errors without a file", pane.Summary, StringComparison.Ordinal);
        window.Close();
    });

    [Fact]
    public Task SpanishTextsAreUnchanged() => HeadlessHost.Run(() =>
    {
        using var scope = CultureScope.Spanish();
        (HeatmapPane pane, Window window, _) = Open(Session(targetsKnown: false));
        Assert.StartsWith("70 archivos, 25 tocados", pane.Summary, StringComparison.Ordinal); // lang-ok
        Assert.Contains("5 errores sin archivo", pane.Summary, StringComparison.Ordinal); // lang-ok
        Assert.Equal("El agente de esta sesión no indica sobre qué archivo falla una llamada", pane.ErrorsTooltip); // lang-ok
        window.Close();
    });

    [Fact]
    public Task AnEmptyMapShowsItsMessageWithoutThrowing() => HeadlessHost.Run(() =>
    {
        var pane = new HeatmapPane(_ => { }, _ => { }, _ => { }, _ => { });
        var window = new Window { Width = 800, Height = 500, Content = pane };
        window.Show();
        Assert.Null(pane.Canvas.Layout);
        Save(window, "heatmap-empty");
        pane.SetTree(TreeOf(Session()), Session());
        pane.Clear();
        Assert.Null(pane.Canvas.Tree);
        Assert.False(pane.DetailVisible);
        window.Close();
    });
}
