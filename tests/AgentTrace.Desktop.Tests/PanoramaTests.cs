using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AgentTrace.Core;
using AgentTrace.Desktop.Controls;
using AgentTrace.Presentation.Graph;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Panorama;
using AgentTrace.Testing;
using static AgentTrace.Desktop.Tests.MainWindowTests;

namespace AgentTrace.Desktop.Tests;

/// <summary>The Panorama tab (graph, Areas map and Knowledge in one layout) with a real project, in a headless window.</summary>
public sealed class PanoramaTests : TempDirTestBase
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    private (GitFixture Repo, FilePath Trace, RecentProjects Recent) Project()
    {
        Assert.SkipUnless(GitFixture.GitAvailable(), "This test needs git installed");
        GitFixture repo = GitFixture.Init(Dir.Combine("project"));
        repo.Write("src/Program.cs", "class Program { static void Main() { } }\n");
        repo.Write("README.md", "# Project\n");
        repo.CommitAll("base");
        FilePath trace = new FilePath(AppContext.BaseDirectory).Combine("TestData", "claude", "projects", "D--fake-proj", Session + ".jsonl");
        return (repo, trace, new RecentProjects(Dir.Combine("history.txt")));
    }

    private MainWindow Open(GitFixture repo, FilePath trace, RecentProjects recent, double width = 1400)
    {
        var window = new MainWindow([new FixedTraceAdapter(trace)], recent, panorama: new PanoramaPreference(Dir.Combine("panorama.txt"))) { Width = width, Height = 850 };
        window.Show();
        window.OpenProject(repo.Dir);
        // Generous: whichever test runs first in the process pays the cold start of the first window (slow on a shared Linux runner)
        try { Pump("the graph", () => window.Graph.Canvas.Graph.StepCount > 0, 20_000); }
        catch (Exception e)
        {
            ThreadPool.GetAvailableThreads(out int free, out _);
            throw new InvalidOperationException($"DIAG steps={window.Graph.Canvas.Graph.StepCount} traceExists={File.Exists(trace.Value)} repoExists={Directory.Exists(repo.Dir.Value)} poolThreads={ThreadPool.ThreadCount} poolPending={ThreadPool.PendingWorkItemCount} poolFree={free} cpus={Environment.ProcessorCount} culture={System.Globalization.CultureInfo.CurrentCulture.Name}", e);
        }
        return window;
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static Point Center(Visual visual, Window window) => visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Control control)
    {
        Point p = Center(control, window);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string name)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(dir);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
    }

    [Fact]
    public Task ThePanoramaHostsTheSamePanesAsTheirTabsAndSwitchingBackAndForthKeepsWorking()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            Settle(window);
            Assert.False(window.Panorama.IsAttached);
            Assert.True(window.Tabs.IsVisualAncestorOf(window.Graph));

            for (int round = 0; round < 3; round++)
            {
                window.SwitchTab(MainWindow.PanoramaTab);
                Settle(window);
                Assert.True(window.Panorama.IsAttached);
                // The three real panes are in the Panorama, with a size, and nowhere else
                foreach (Control pane in new Control[] { window.Graph, window.Areas, window.Knowledge })
                {
                    Assert.True(window.Panorama.IsVisualAncestorOf(pane));
                    Assert.True(pane.IsVisible);
                    Assert.True(pane.Bounds.Width > 100 && pane.Bounds.Height > 100, pane.GetType().Name + " " + pane.Bounds);
                }
                Assert.True(window.Graph.Bounds.Right <= window.Areas.Bounds.Left + window.Graph.Bounds.Width); // graph left, Areas and Knowledge right
                Assert.Equal(window.Areas.TranslatePoint(default, window)!.Value.X, window.Knowledge.TranslatePoint(default, window)!.Value.X);
                Assert.True(window.Areas.TranslatePoint(default, window)!.Value.Y < window.Knowledge.TranslatePoint(default, window)!.Value.Y);

                // The state of a pane survives the trip
                window.Graph.Canvas.SetZoom(0.75 + 0.25 * round);

                foreach (int tab in new[] { MainWindow.GraphTab, MainWindow.KnowledgeTab, MainWindow.AreasTab })
                {
                    window.SwitchTab(tab);
                    Settle(window);
                    Assert.False(window.Panorama.IsAttached);
                    Assert.False(window.Panorama.IsVisualAncestorOf(window.Graph));
                    Assert.False(window.Panorama.IsVisualAncestorOf(window.Areas));
                    Assert.False(window.Panorama.IsVisualAncestorOf(window.Knowledge));
                    Control shown = tab switch { MainWindow.GraphTab => window.Graph, MainWindow.KnowledgeTab => window.Knowledge, _ => window.Areas };
                    Assert.True(shown.Bounds.Width > 500, shown.GetType().Name + " " + shown.Bounds);
                }
                Assert.Equal(0.75 + 0.25 * round, window.Graph.Canvas.Viewport.Zoom, 3);
            }
            window.Close();
        });
    }

    [Fact]
    public Task TheKeyboardAndTheTabNameOpenThePanorama()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            window.KeyPress(Key.D8, RawInputModifiers.Control, PhysicalKey.Digit8, "8");
            Assert.Equal(MainWindow.PanoramaTab, window.Tabs.SelectedIndex);
            Assert.True(window.Panorama.IsAttached);
            window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, "1");
            Assert.Equal(MainWindow.GraphTab, window.Tabs.SelectedIndex);
            Assert.False(window.Panorama.IsAttached);
            window.Close();
        });
    }

    [Fact]
    public Task TheTogglesHideAndShowPanesAndTheOneThatAppearsIsBroughtUpToDate()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            window.SwitchTab(MainWindow.PanoramaTab);
            Settle(window);
            PanoramaPane panorama = window.Panorama;

            // Both panes are in view: the same git change (no event of the trace explains it) shows in the Knowledge graph and in the Areas map
            int version = 0;
            FilePath first = repo.Dir.Combine("first.txt");
            Pump("the first file in Knowledge and Areas", () =>
            {
                File.WriteAllText(first.Value, "first " + (++version) + "\n");
                window.SwitchTab(MainWindow.GraphTab); // the 1 s timer does not run headless: coming back requests the repaint
                window.SwitchTab(MainWindow.PanoramaTab);
                return window.Knowledge.Canvas.Graph.FindNode(first) is not null && window.Areas.Canvas.Matrix.Rows.Any(r => r.Files > 0);
            }, 20_000);

            // Knowledge off: it disappears, the other two share the space and nothing refreshes it
            Click(window, panorama.KnowledgeToggle);
            Settle(window);
            Assert.False(panorama.KnowledgeVisible);
            Assert.False(window.Knowledge.IsVisible);
            Assert.True(window.Graph.IsVisible && window.Areas.IsVisible);
            Assert.False(panorama.RowSplitter.IsVisible);
            Assert.True(window.Areas.Bounds.Height > window.Graph.Bounds.Height * 0.8, "Areas takes the whole right column: " + window.Areas.Bounds);

            FilePath second = repo.Dir.Combine("second.txt");
            Pump("the second file in the tree", () =>
            {
                File.WriteAllText(second.Value, "second " + (++version) + "\n");
                return window.Tree.VisibleNodes().Any(n => n.Node.Name == "second.txt");
            }, 20_000);
            // A repaint timer (1 s) scheduled while Knowledge was still in view must not run once it is hidden: wait past it
            long hold = Environment.TickCount64 + 1_500;
            Pump("the repaint timer of the hidden pane to pass", () => Environment.TickCount64 >= hold);
            Assert.Null(window.Knowledge.Canvas.Graph.FindNode(second));

            // Knowledge on again: it is brought up to date at once
            Click(window, panorama.KnowledgeToggle);
            Pump("the second file in Knowledge", () =>
            {
                File.WriteAllText(second.Value, "second " + (++version) + "\n");
                return window.Knowledge.Canvas.Graph.FindNode(second) is not null;
            }, 20_000);
            Assert.True(window.Knowledge.IsVisible);

            // Hiding the graph leaves the right column alone; the last visible pane cannot be hidden
            Click(window, panorama.GraphToggle);
            Settle(window);
            Assert.False(window.Graph.IsVisible);
            Assert.False(panorama.ColumnSplitter.IsVisible);
            Assert.True(window.Areas.Bounds.Width > window.Panorama.Bounds.Width * 0.9, window.Areas.Bounds.ToString());
            Click(window, panorama.AreasToggle);
            Click(window, panorama.KnowledgeToggle);
            Assert.True(panorama.GraphVisible || panorama.AreasVisible || panorama.KnowledgeVisible);

            // Leaving the Panorama gives the panes back visible, whatever was hidden
            window.SwitchTab(MainWindow.GraphTab);
            Settle(window);
            Assert.True(window.Graph.IsVisible && window.Areas.IsVisible && window.Knowledge.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public Task TheSplittersResizeTheColumnAndTheRows()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            window.SwitchTab(MainWindow.PanoramaTab);
            Settle(window);
            PanoramaPane panorama = window.Panorama;
            double graphWidth = window.Graph.Bounds.Width;
            double areasHeight = window.Areas.Bounds.Height;

            Point p = Center(panorama.ColumnSplitter, window);
            window.MouseDown(p, MouseButton.Left);
            window.MouseMove(p + new Point(150, 0));
            window.MouseUp(p + new Point(150, 0), MouseButton.Left);
            Settle(window);
            Assert.True(window.Graph.Bounds.Width > graphWidth + 100, $"{graphWidth} -> {window.Graph.Bounds.Width}");

            Point r = Center(panorama.RowSplitter, window);
            window.MouseDown(r, MouseButton.Left);
            window.MouseMove(r + new Point(0, 100));
            window.MouseUp(r + new Point(0, 100), MouseButton.Left);
            Settle(window);
            Assert.True(window.Areas.Bounds.Height > areasHeight + 60, $"{areasHeight} -> {window.Areas.Bounds.Height}");

            // The new sizes survive hiding and showing a pane
            double widened = window.Graph.Bounds.Width;
            Click(window, panorama.AreasToggle);
            Click(window, panorama.AreasToggle);
            Settle(window);
            Assert.Equal(widened, window.Graph.Bounds.Width, 1);
            window.Close();
        });
    }

    [Fact]
    public Task InANarrowPaneTheDetailPanelsDockAtTheBottomAndInAWideOneAtTheRight()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent, width: 1100);
            window.Graph.Canvas.Select(GraphSelection.Parse("last", window.Graph.Canvas.Graph));
            Settle(window);
            Assert.True(window.Graph.DetailVisible);
            Control host = (Control)window.Graph.Detail.Parent!;

            // On its own tab the graph is ~800 px wide: not enough for the canvas and the 340 px panel twice over
            window.SwitchTab(MainWindow.GraphTab);
            Settle(window);
            Assert.Equal(window.Graph.Bounds.Width >= 2 * DetailPane.PaneWidth ? Avalonia.Controls.Dock.Right : Avalonia.Controls.Dock.Bottom, DockPanel.GetDock(host));

            // In the Panorama it is a third of the window
            window.SwitchTab(MainWindow.PanoramaTab);
            Settle(window);
            Assert.True(window.Graph.Bounds.Width < 2 * DetailPane.PaneWidth, window.Graph.Bounds.ToString());
            Assert.Equal(Avalonia.Controls.Dock.Bottom, DockPanel.GetDock(host));
            Assert.True(window.Graph.Detail.Bounds.Height <= DetailDock.BottomMaxHeight + 1);
            Assert.True(window.Graph.Canvas.Bounds.Height > 150, window.Graph.Canvas.Bounds.ToString());
            Save(window, "panorama-narrow");

            window.Width = 2400;
            Settle(window);
            Assert.True(window.Graph.Bounds.Width >= 2 * DetailPane.PaneWidth, window.Graph.Bounds.ToString());
            Assert.Equal(Avalonia.Controls.Dock.Right, DockPanel.GetDock(host));
            window.Close();
        });
    }

    private FilePath PreferenceFile => Dir.Combine("panorama.txt");

    [Fact]
    public Task TheSavedLayoutIsRestoredWhenTheWindowOpens()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        File.WriteAllText(PreferenceFile.Value, "graph=true\nareas=false\nknowledge=true\ncolumn=0.6\nrow=0.5\n");
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            window.SwitchTab(MainWindow.PanoramaTab);
            Settle(window);
            PanoramaPane panorama = window.Panorama;
            Assert.True(panorama.GraphVisible);
            Assert.False(panorama.AreasVisible);
            Assert.True(panorama.KnowledgeVisible);
            Assert.False(window.Areas.IsVisible);
            Assert.Equal(0.6, panorama.Layout.ColumnRatio, 3);
            Assert.Equal(0.6, window.Graph.Bounds.Width / (window.Graph.Bounds.Width + window.Knowledge.Bounds.Width), 1);
            window.Close();
        });
    }

    [Fact]
    public Task ACorruptFileMeansTheDefaultLayoutWithTheRatiosKeptInRange()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        File.WriteAllText(PreferenceFile.Value, "\0\0 not a layout =\ngraph=maybe\ncolumn=9\n");
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            Assert.Equal(PanoramaLayout.Default with { ColumnRatio = PanoramaLayout.MaxRatio }, window.Panorama.Layout);
            window.Close();
        });
    }

    [Fact]
    public Task TogglesAndSplittersSaveTheLayoutAndANewWindowRestoresIt()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            window.SwitchTab(MainWindow.PanoramaTab);
            Settle(window);
            Assert.False(File.Exists(PreferenceFile.Value)); // opening and showing the Panorama write nothing

            Click(window, window.Panorama.KnowledgeToggle);
            Assert.Contains("knowledge=false", File.ReadAllText(PreferenceFile.Value), StringComparison.Ordinal);
            Settle(window);
            Point p = Center(window.Panorama.ColumnSplitter, window);
            window.MouseDown(p, MouseButton.Left);
            window.MouseMove(p + new Point(150, 0));
            window.MouseUp(p + new Point(150, 0), MouseButton.Left);
            Settle(window);
            PanoramaLayout saved = new PanoramaPreference(PreferenceFile).Load();
            Assert.False(saved.Knowledge);
            Assert.True(saved.ColumnRatio > PanoramaLayout.Default.ColumnRatio + 0.05, saved.ColumnRatio.ToString(System.Globalization.CultureInfo.InvariantCulture));
            window.Close();

            MainWindow again = Open(repo, trace, recent);
            Assert.False(again.Panorama.KnowledgeVisible);
            Assert.Equal(saved.ColumnRatio, again.Panorama.Layout.ColumnRatio, 3);
            again.Close();
        });
    }

    [Fact]
    public Task StartupOptionsShowOnlyTheNamedPanesWithoutSavingAndLinkTheFocus()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            window.ApplyPanoramaOptions(StartupOptions.Parse(["--panorama=graph,areas", "--panorama-focus=turn:1"]));
            window.SwitchTab(MainWindow.PanoramaTab);
            Settle(window);
            Assert.True(window.Panorama.GraphVisible);
            Assert.True(window.Panorama.AreasVisible);
            Assert.False(window.Panorama.KnowledgeVisible);
            Assert.False(window.Knowledge.IsVisible);
            Assert.Equal(FocusKind.Turn, window.LinkedFocus.Current!.Kind);
            Assert.False(File.Exists(PreferenceFile.Value)); // an option is for this run only
            window.Close();
        });
    }

    [Fact]
    public Task APanoramaFocusIsCaseInsensitiveLikeTheOptionParser()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            window.ApplyPanoramaOptions(StartupOptions.Parse(["--panorama-focus=Turn:1"]));
            window.SwitchTab(MainWindow.PanoramaTab);
            Settle(window);
            Assert.Equal(FocusKind.Turn, window.LinkedFocus.Current!.Kind);
            window.Close();
        });
    }

    [Fact]
    public Task AnExplicitPaneOptionWinsOverThePanoramaFocus()
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            MainWindow window = Open(repo, trace, recent);
            StartupOptions options = StartupOptions.Parse(["--kg-select=new.txt", "--panorama-focus=toolu_1"]);
            window.SelectInKnowledge(options.KnowledgeSelect!); // what the startup does for --kg-select, before the file exists
            window.ApplyPanoramaOptions(options);
            File.WriteAllText(repo.Dir.Combine("new.txt").Value, "new\n");
            Pump("the explicit Knowledge selection", () =>
            {
                window.SwitchTab(MainWindow.GraphTab);
                window.SwitchTab(MainWindow.KnowledgeTab);
                return window.Knowledge.Canvas.SelectedPath == repo.Dir.Combine("new.txt");
            }, 20_000);
            window.Close();
        });
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("es-ES", "es")]
    public Task ThePanoramaIsCapturedInEachLanguage(string culture, string lang)
    {
        (GitFixture repo, FilePath trace, RecentProjects recent) = Project();
        return HeadlessHost.Run(() =>
        {
            using var _ = new CultureScope(culture);
            MainWindow window = Open(repo, trace, recent);
            window.SwitchTab(MainWindow.PanoramaTab);
            window.Graph.Canvas.Select(GraphSelection.Parse("last", window.Graph.Canvas.Graph));
            Pump("Knowledge and Areas", () =>
            {
                File.WriteAllText(repo.Dir.Combine("new.txt").Value, "new\n");
                window.SwitchTab(MainWindow.GraphTab);
                window.SwitchTab(MainWindow.PanoramaTab);
                return window.Knowledge.Canvas.Graph.Nodes.Count > 0 && window.Areas.Canvas.Matrix.Rows.Any(r => r.Files > 0);
            }, 20_000);
            Settle(window);
            Save(window, "panorama-" + lang);
            Assert.Equal(culture.StartsWith("es", StringComparison.Ordinal) ? "Vista global" : "Panorama", (string)((TabItem)window.Tabs.Items[MainWindow.PanoramaTab]!).Header!); // lang-ok
            window.Close();
        });
    }
}
