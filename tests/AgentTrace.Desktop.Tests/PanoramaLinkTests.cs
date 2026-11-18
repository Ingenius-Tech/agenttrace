using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Core.Flow;
using AgentTrace.Core.Knowledge;
using AgentTrace.Desktop.Controls;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Graph;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Panorama;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Desktop.Tests;

/// <summary>
/// Linked selection inside the Panorama: a synthetic session of 40 turns is fed straight to the three panes of a window without a project, so
/// what each pane shows is known exactly. Selecting in one pane must highlight the related items in the other two, only in the Panorama.
/// </summary>
public sealed class PanoramaLinkTests : TempDirTestBase
{
    private const int Turns = 40;
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\proj" : "/proj");

    private static FilePath F(string relative) => Root.Combine(relative.Split('/'));

    private static readonly FilePath Shared = F("src/App/shared.cs");

    private static EventHeader H(int second) => new(new SessionId("s"), "test", null, T0.AddSeconds(second), new SourceRef(new FilePath("t"), 1));

    /// <summary>
    /// Turn t: call "c{t}" writes <c>src/App/f{t}.cs</c> (even t) or <c>src/Data/f{t}.cs</c> (odd t). Turns 3 and 30 also read the shared file with "r3" and "r30".
    /// </summary>
    private static IEnumerable<AgentEvent> Events()
    {
        for (int t = 0; t < Turns; t++)
        {
            int s = t * 10;
            yield return new UserMessage(H(s), "task " + t);
            yield return new ToolCall(H(s + 1), "c" + t, "Write", ToolKind.Write, "c" + t, "{}");
            yield return new FileChanged(H(s + 1), F((t % 2 == 0 ? "src/App/f" : "src/Data/f") + t + ".cs"), ChangeKind.Modified, "c" + t);
            if (t != 3 && t != 30) continue;
            yield return new ToolCall(H(s + 2), "r" + t, "Read", ToolKind.Read, "r" + t, "{}");
            yield return new FileRead(H(s + 2), Shared, "r" + t);
        }
    }

    private static AreaRepo Layered()
    {
        var set = new AreaRules(new[]
        {
            new AreaRule("data", "Data", AreaDimension.Layer, new[] { "src/Data/**" }.ToValueList(), ValueList<string>.Empty),
            new AreaRule("app", "App", AreaDimension.Layer, new[] { "src/App/**" }.ToValueList(), ValueList<string>.Empty),
        }.ToValueList());
        return new AreaRepo(Root, "proj", set, new AreaClassifier(Root, set), AreaSource.File, ValueList<AreaRuleError>.Empty);
    }

    private MainWindow Open(double width = 1400)
    {
        var flow = new FlowBuilder();
        var knowledge = new KnowledgeBuilder(Root);
        foreach (AgentEvent e in Events())
        {
            flow.Accept(e);
            knowledge.Accept(e);
        }
        FlowGraph graph = flow.Snapshot();
        var window = new MainWindow([], new RecentProjects(Dir.Combine("history.txt"))) { Width = width, Height = 850 };
        window.Show();
        window.Graph.SetGraph(graph);
        window.Knowledge.SetProjectRoot(Root);
        window.Knowledge.SetGraph(knowledge.Snapshot());
        window.Areas.SetDimension(AreaDimension.Layer);
        window.Areas.SetSnapshot(AreasSnapshot.Of(graph, [], new AreaRepoSet([Layered()]), Root));
        Settle(window);
        return window;
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void ShowPanorama(MainWindow window)
    {
        window.SwitchTab(MainWindow.PanoramaTab);
        Settle(window);
        Assert.True(window.Panorama.IsAttached);
    }

    private static void Save(Window window, string name)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(dir);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void AssertNoHighlights(MainWindow window)
    {
        Assert.Null(window.Graph.Canvas.LinkedFocus);
        Assert.Null(window.Areas.Canvas.LinkedFocus);
        Assert.Null(window.Knowledge.Canvas.LinkedFocus);
    }

    [Fact]
    public Task AGraphStepHighlightsItsTurnAndRowInAreasAndItsFilesInKnowledgeAndScrollsAreasToIt() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);
        Assert.Equal(0, window.Areas.Canvas.ScrollX);

        window.Graph.Canvas.Select(GraphSelection.ForStep("c31"));
        Settle(window);

        // Knowledge: only the step's file stays lit; Areas: the turn column and the Data row, scrolled into view
        Assert.Equal([F("src/Data/f31.cs")], window.Knowledge.Canvas.LinkedFocus!);
        FocusTargets areas = window.Areas.Canvas.LinkedFocus!;
        Assert.Equal((31, 31), (areas.FirstTurn, areas.LastTurn));
        Assert.Equal(["data"], areas.AreaRows.Select(r => r.AreaId).ToArray());
        Assert.True(window.Areas.Canvas.ScrollX > 0, "the Areas map scrolled to the turn");
        int column = window.Areas.Canvas.Matrix.ColumnOfTurn(31);
        double x = AreasGeometry.ColumnX(column, window.Areas.Canvas.ScrollX, window.Areas.Canvas.Zoom);
        Assert.InRange(x, AreasGeometry.HeaderWidth, window.Areas.Canvas.Bounds.Width);
        // The origin pane shows no link of its own, and nothing was selected anywhere else
        Assert.Null(window.Graph.Canvas.LinkedFocus);
        Assert.Null(window.Knowledge.Canvas.SelectedPath);
        Assert.Null(window.Areas.Canvas.SelectedCell);
        Assert.False(window.Areas.PanelVisible);
        Assert.Equal(FocusKind.Step, window.LinkedFocus.Current!.Kind);
        Assert.True(window.Panorama.FocusActive);
        Save(window, "panorama-link-step");
        window.Close();
    });

    [Fact]
    public Task AKnowledgeFileDimsTheOtherStepsInTheGraphAndHighlightsItsTurnsInAreas() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);

        window.Knowledge.Canvas.Select(Shared);
        Settle(window);

        FocusTargets graph = window.Graph.Canvas.LinkedFocus!;
        Assert.True(new HashSet<string> { "r3", "r30" }.SetEquals(graph.CallIds));
        Assert.Equal((3, 30), (graph.FirstTurn, graph.LastTurn));
        Assert.Equal((3, 30), (window.Areas.Canvas.LinkedFocus!.FirstTurn, window.Areas.Canvas.LinkedFocus.LastTurn));
        Assert.Equal(["app"], window.Areas.Canvas.LinkedFocus.AreaRows.Select(r => r.AreaId).ToArray());
        Assert.Null(window.Knowledge.Canvas.LinkedFocus);
        Assert.Equal(Shared, window.Knowledge.Canvas.SelectedPath);
        Save(window, "panorama-link-file");
        window.Close();
    });

    [Fact]
    public Task AnAreasCellATaskAndATurnHeaderEachLinkTheOtherPanes() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);
        AreasCanvasView canvas = window.Areas.Canvas;

        // A real click on the cell of the App row in turn 30
        int row = canvas.Matrix.Rows.ToList().FindIndex(r => r.Key.AreaId == "app");
        int column = canvas.Matrix.ColumnOfTurn(30);
        canvas.RevealColumn(30);
        Settle(window);
        int line = canvas.Lines.ToList().FindIndex(l => l.Row == row && l.File is null);
        Point p = canvas.TranslatePoint(canvas.CenterOf(line, column), window)!.Value;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Settle(window);
        Assert.Equal((row, column), canvas.SelectedCell);
        Assert.Equal(FocusKind.AreaCell, window.LinkedFocus.Current!.Kind);
        Assert.True(new HashSet<string> { "c30", "r30" }.SetEquals(window.Graph.Canvas.LinkedFocus!.CallIds));
        Assert.True(new HashSet<FilePath> { F("src/App/f30.cs"), Shared }.SetEquals(window.Knowledge.Canvas.LinkedFocus!));
        Assert.Null(canvas.LinkedFocus);

        // A task: its turn range
        canvas.SelectTask(5);
        Settle(window);
        Assert.Equal(FocusKind.Task, window.LinkedFocus.Current!.Kind);
        Assert.Equal(["c5"], window.Graph.Canvas.LinkedFocus!.CallIds);
        Assert.Equal([F("src/Data/f5.cs")], window.Knowledge.Canvas.LinkedFocus!);

        // A turn header
        canvas.RevealColumn(12);
        Settle(window);
        int c12 = canvas.Matrix.ColumnOfTurn(12);
        Point h = canvas.TranslatePoint(new Point(AreasGeometry.ColumnX(c12, canvas.ScrollX, canvas.Zoom) + 10, AreasGeometry.TaskBandHeight + 8), window)!.Value;
        window.MouseDown(h, MouseButton.Left);
        window.MouseUp(h, MouseButton.Left);
        Settle(window);
        Assert.Equal(new TraceFocus(FocusOrigin.Areas, FocusKind.Turn, Turn: 12), window.LinkedFocus.Current);
        Assert.Equal(["c12"], window.Graph.Canvas.LinkedFocus!.CallIds);
        Assert.Equal([F("src/App/f12.cs")], window.Knowledge.Canvas.LinkedFocus!);
        window.Close();
    });

    [Fact]
    public Task AGraphTurnScrollsTheGraphToItsTurnWhenAnotherPaneIsTheOrigin() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);
        window.Graph.Canvas.GoToEnd();
        Settle(window);
        FlowCanvasView graph = window.Graph.Canvas;

        window.Areas.Canvas.SelectTask(3);
        Settle(window);

        // The graph did not select anything or fold anything, but its view now shows turn 3
        Assert.Null(graph.Selection);
        GraphGeometry.Box? box = graph.Geometry.FindTurnBox(3, GraphGeometry.Kind.Prompt) ?? graph.Geometry.FindTurnBox(3, GraphGeometry.Kind.Folded);
        Assert.NotNull(box);
        Assert.True(graph.Viewport.VisibleRect().Y <= box.Y + box.H && box.Y <= graph.Viewport.VisibleRect().Y + graph.Viewport.VisibleRect().H,
            $"turn 3 at y={box.Y} is outside the view {graph.Viewport.VisibleRect()}");
        window.Close();
    });

    [Fact]
    public Task OutsideThePanoramaNothingLinksAndLeavingItClearsTheHighlightsButKeepsTheSelections() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        Assert.False(window.LinkedFocus.Active);

        // On the separate tabs each pane behaves as before: no highlight anywhere, no focus kept
        window.SwitchTab(MainWindow.GraphTab);
        window.Graph.Canvas.Select(GraphSelection.ForStep("c2"));
        window.SwitchTab(MainWindow.KnowledgeTab);
        window.Knowledge.Canvas.Select(Shared);
        window.SwitchTab(MainWindow.AreasTab);
        window.Areas.Canvas.SelectTask(7);
        Settle(window);
        AssertNoHighlights(window);
        Assert.Null(window.LinkedFocus.Current);

        // Showing the Panorama links what is already selected (a pane's own selection predates it); the latest in the priority order is the graph's
        ShowPanorama(window);
        Assert.True(window.LinkedFocus.Active);
        Assert.Equal(TraceFocus.ForStep("c2"), window.LinkedFocus.Current);
        Assert.NotNull(window.Knowledge.Canvas.LinkedFocus);
        Assert.NotNull(window.Areas.Canvas.LinkedFocus);

        // Leaving clears every highlight; the selections themselves stay
        window.SwitchTab(MainWindow.GraphTab);
        Settle(window);
        Assert.False(window.LinkedFocus.Active);
        AssertNoHighlights(window);
        Assert.Null(window.LinkedFocus.Current);
        Assert.False(window.Panorama.FocusActive);
        Assert.Equal(GraphSelection.ForStep("c2"), window.Graph.Canvas.Selection);
        Assert.Equal(Shared, window.Knowledge.Canvas.SelectedPath);
        Assert.Equal(7, window.Areas.Canvas.SelectedTask);

        // A selection on a separate tab again does nothing
        window.Graph.Canvas.Select(GraphSelection.ForStep("c4"));
        AssertNoHighlights(window);
        window.Close();
    });

    [Fact]
    public Task EachUserActionRaisesExactlyOneSelectionAndALinkNeverRaisesOneBack() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);
        int graph = 0, knowledge = 0, cells = 0, tasks = 0;
        window.Graph.Canvas.Selected += _ => graph++;
        window.Knowledge.Canvas.Selected += _ => knowledge++;
        window.Areas.Canvas.CellSelectionChanged += () => cells++;
        window.Areas.Canvas.TaskSelectionChanged += () => tasks++;

        window.Graph.Canvas.Select(GraphSelection.ForStep("c2"));
        Assert.Equal((1, 0, 0, 0), (graph, knowledge, cells, tasks));
        window.Knowledge.Canvas.Select(Shared);
        Assert.Equal((1, 1, 0, 0), (graph, knowledge, cells, tasks));
        window.Areas.Canvas.SelectTask(9);
        Assert.Equal((1, 1, 0, 1), (graph, knowledge, cells, tasks));
        window.Areas.Canvas.Select(0, 0);
        Assert.Equal((1, 1, 1, 1), (graph, knowledge, cells, tasks));

        // Many alternating selections finish (no loop) and leave the last one as the focus
        for (int i = 0; i < 30; i++)
        {
            window.Graph.Canvas.Select(GraphSelection.ForStep("c" + (i % Turns)));
            window.Knowledge.Canvas.Select(i % 2 == 0 ? Shared : F("src/App/f" + (i % 2 == 0 ? i : i + 1) + ".cs"));
            window.Areas.Canvas.SelectTask(i % Turns);
        }
        Assert.Equal(1 + 30, graph);
        Assert.Equal(1 + 30, knowledge);
        Assert.Equal(1 + 30, tasks);
        Assert.Equal(FocusKind.Task, window.LinkedFocus.Current!.Kind);
        Assert.False(window.LinkedFocus.Applying);

        // Applying the same focus again (a refresh) changes nothing and raises nothing
        int before = graph + knowledge + cells + tasks;
        window.Graph.Canvas.ShowFocus(window.Graph.Canvas.LinkedFocus);
        window.Knowledge.Canvas.ShowFocus(window.Knowledge.Canvas.LinkedFocus);
        window.Areas.Canvas.ShowFocus(window.Areas.Canvas.LinkedFocus);
        Assert.Equal(before, graph + knowledge + cells + tasks);
        window.Close();
    });

    [Fact]
    public Task ClearingTheLinkRemovesTheHighlightsAndKeepsTheSelection() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);
        Assert.False(window.Panorama.ClearFocusButton.IsVisible);
        window.Knowledge.Canvas.Select(Shared);
        Settle(window);
        Assert.True(window.Panorama.ClearFocusButton.IsVisible);
        Assert.NotNull(window.Graph.Canvas.LinkedFocus);

        Point p = window.Panorama.ClearFocusButton.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Settle(window);

        AssertNoHighlights(window);
        Assert.Null(window.LinkedFocus.Current);
        Assert.False(window.Panorama.ClearFocusButton.IsVisible);
        Assert.Equal(Shared, window.Knowledge.Canvas.SelectedPath);
        // Clearing the selection itself in the pane publishes nothing new
        window.Knowledge.Canvas.Select(null);
        AssertNoHighlights(window);
        window.Close();
    });

    [Fact]
    public Task ClearingASelectionInThePaneClearsTheHighlightsInTheOthers() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);
        window.Graph.Canvas.Select(GraphSelection.ForStep("c2"));
        Assert.NotNull(window.Knowledge.Canvas.LinkedFocus);
        window.Graph.Canvas.Select(null);
        Settle(window);
        AssertNoHighlights(window);
        Assert.Null(window.LinkedFocus.Current);
        window.Close();
    });

    [Fact]
    public Task TheLinkIsKeptWhenANewMatrixArrivesAndIsRecomputedForIt() => HeadlessHost.Run(() =>
    {
        MainWindow window = Open();
        ShowPanorama(window);
        window.Knowledge.Canvas.Select(Shared);
        Settle(window);
        Assert.NotNull(window.Areas.Canvas.LinkedFocus);

        // The user regroups the Areas map by another dimension: the rows change, the focus is not lost
        window.Areas.Canvas.SetMatrix(AreasMatrix.Build(window.Areas.Canvas.Matrix.Snapshot, AreaDimension.Module, window.Areas.Filter));
        Assert.Equal(Shared, window.Knowledge.Canvas.SelectedPath);
        Assert.Equal(TraceFocus.ForFile(Shared), window.LinkedFocus.Current);
        window.Close();
    });

    [Fact]
    public Task ThePanoramaWithLinkedSelectionIsCapturedInEnglishAndSpanish() => HeadlessHost.Run(() =>
    {
        foreach ((string culture, string lang) in new[] { ("en-US", "en"), ("es-ES", "es") })
        {
            using var _ = new CultureScope(culture);
            MainWindow window = Open();
            ShowPanorama(window);
            window.Areas.Canvas.SelectTask(30);
            Settle(window);
            Save(window, "panorama-link-task-" + lang);
            window.Close();
        }
    });
}
