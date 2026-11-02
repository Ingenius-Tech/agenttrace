using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AgentTrace.Adapters.ClaudeCode;
using AgentTrace.Core;
using AgentTrace.Core.Search;
using AgentTrace.Desktop.Controls;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Resources;
using AgentTrace.Testing;

namespace AgentTrace.Desktop.Tests;

/// <summary>
/// The whole window, end to end and without a screen: a project with real git, an agent whose trace is the synthetic Claude Code
/// one and the six tabs populated.
/// </summary>
public sealed class MainWindowTests : TempDirTestBase
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    /// <summary>An agent that always follows the same trace (the synthetic one) with the real Claude Code parser.</summary>
    internal sealed class FixedTraceAdapter(FilePath trace) : IAgentAdapter
    {
        private readonly ClaudeCodeAdapter _real = new();

        public string Agent => "claude-code";

        public bool Owns(FilePath traceFile) => traceFile == trace;

        public IReadOnlyList<FilePath> FindTraceFiles(FilePath projectDir) => [trace];

        public IReadOnlyList<FilePath> RelatedTraceFiles(FilePath sessionTrace) => _real.RelatedTraceFiles(sessionTrace);

        public ILineParser NewParser(FilePath traceFile) => _real.NewParser(traceFile);
    }

    private static string CapturesDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Processes what is pending on the UI thread (observer notifications, timers) until the condition is met.</summary>
    internal static void Pump(string what, Func<bool> condition, int timeoutMillis = 15_000)
    {
        long limit = Environment.TickCount64 + timeoutMillis;
        while (Environment.TickCount64 < limit)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            Thread.Sleep(20);
        }
        Assert.Fail("Timed out waiting for: " + what);
    }

    /// <summary>
    /// Writes a file the application may be reading right now (its poll opens the file without write sharing): a collision is
    /// retried instead of failing the test (the rewrite loop of the two-repository test writes every 20 ms).
    /// </summary>
    private static void WriteText(string path, string text)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, text);
                return;
            }
            catch (IOException) when (attempt < 50)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static void Save(Window window, string name)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(CapturesDir(), name + ".png"), new PngBitmapEncoderOptions());
    }

    [Fact]
    public Task AnObservedProjectFillsTheSixTabsAndTheKeyboardSwitchesThem()
    {
        Assert.SkipUnless(GitFixture.GitAvailable(), "This test needs git installed");
        GitFixture repo = GitFixture.Init(Dir.Combine("project"));
        repo.Write("src/Program.cs", "class Program { static void Main() { } }\n");
        repo.Write("README.md", "# Project\n");
        repo.CommitAll("base");
        FilePath trace = new FilePath(AppContext.BaseDirectory).Combine("TestData", "claude", "projects", "D--fake-proj", Session + ".jsonl");
        var recent = new RecentProjects(Dir.Combine("history.txt"));

        return HeadlessHost.Run(() =>
        {
            var window = new MainWindow([new FixedTraceAdapter(trace)], recent) { Width = 1400, Height = 850 };
            window.Show();
            window.OpenProject(repo.Dir);

            // The trace arrives and feeds the telemetry, the graph, the knowledge and the status bar
            Pump("events in the telemetry", () => window.Telemetry.Items.Count > 5);
            Pump("the graph", () => window.Graph.Canvas.Graph.StepCount > 0);
            Assert.Equal(2, window.Graph.Canvas.Graph.ErrorCount);
            Assert.True(window.Stats.Events > 0);
            Assert.Equal([repo.Dir], recent.List()); // the project stays in the history

            // The tree lists the project
            Pump("the tree", () => window.Tree.VisibleNodes().Any(n => n.Node.Name == "README.md"));
            Assert.Contains(window.Tree.VisibleNodes(), n => n.Node.Name == "src");

            // A project file opens in its tab with the real content
            window.OpenFile("README.md");
            Pump("the open file", () => window.FileView.Current is not null);
            Assert.Equal(MainWindow.FileTab, window.Tabs.SelectedIndex);
            Pump("its content", () => window.FileView.Viewer.Text.Contains("# Project", StringComparison.Ordinal));

            // Ctrl+1..6 switch tabs (the sixth is the heatmap)
            for (int i = 1; i <= 6; i++)
            {
                Key key = Key.D1 + (i - 1);
                window.KeyPress(key, RawInputModifiers.Control, PhysicalKey.Digit1 + (i - 1), i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(i - 1, window.Tabs.SelectedIndex);
                Dispatcher.UIThread.RunJobs();
                Save(window, "main-tab" + i);
            }

            // The heatmap (Ctrl+6) lists the real project and updates live when a new file appears on disk
            window.SwitchTab(MainWindow.HeatmapTab);
            Pump("the heatmap", () => window.Heatmap.Canvas.Tree?.Find(repo.Dir.Combine("README.md")) is not null, 20_000);
            Assert.NotNull(window.Heatmap.Canvas.Tree!.Find(repo.Dir.Combine("src", "Program.cs")));
            Assert.Null(window.Heatmap.Canvas.Tree.Find(repo.Dir.Combine("new.txt")));
            int files = window.Heatmap.Canvas.Tree.Root.Files;
            File.WriteAllText(repo.Dir.Combine("new.txt").Value, "just created\n");
            Pump("the new file in the tree", () => window.Tree.VisibleNodes().Any(n => n.Node.Name == "new.txt"), 20_000);
            // The 1 s timer refresh does not run in the headless dispatcher (the 300 ms ones do): the repaint is requested by coming back to the tab
            window.SwitchTab(MainWindow.GraphTab);
            window.SwitchTab(MainWindow.HeatmapTab);
            Pump("the new file in the map", () => window.Heatmap.Canvas.Tree?.Find(repo.Dir.Combine("new.txt")) is not null, 20_000);
            Assert.Equal(files + 1, window.Heatmap.Canvas.Tree!.Root.Files);
            Assert.Contains((files + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), window.Heatmap.Summary, StringComparison.Ordinal);
            Save(window, "main-heatmap-live");

            // The knowledge is only computed with its tab in view, and only counts the project's files: the synthetic trace
            // touches files elsewhere (C:\p\…), so the graph is empty and says so
            window.SwitchTab(MainWindow.KnowledgeTab);
            Pump("the knowledge graph", () => window.Knowledge.Summary.StartsWith("0 ", StringComparison.Ordinal));
            Assert.Empty(window.Knowledge.Canvas.Graph.Nodes);

            // A real change on disk appears in the Inspector and its diff has the expected rows
            File.WriteAllText(repo.Dir.Combine("README.md").Value, "# Project\nnew line ñ\n"); // lang-ok: non-ASCII content
            Pump("the change in the Inspector", () => window.Diff.Items.Any(e => e.Change.Path.FileName == "README.md"), 20_000);
            window.Diff.Select(repo.Dir.Combine("README.md"));
            Pump("the diff", () => window.Diff.Viewer.Text.Contains("+new line ñ", StringComparison.Ordinal), 20_000); // lang-ok: non-ASCII content
            window.SwitchTab(MainWindow.DiffTab);
            Dispatcher.UIThread.RunJobs();
            Save(window, "main-diff-real");

            window.Close();
        });
    }

    [Fact]
    public Task AFolderWithoutGitHoldingTwoRepositoriesShowsTheChangesOfBothInTheInspector()
    {
        Assert.SkipUnless(GitFixture.GitAvailable(), "This test needs git installed");
        FilePath root = Dir.Combine("project");
        GitFixture api = GitFixture.Init(root.Combine("api"));
        api.Write("Program.cs", "class Api { }\n");
        api.CommitAll("base");
        GitFixture web = GitFixture.Init(root.Combine("web"));
        web.Write("index.html", "<h1>hello</h1>\n");
        web.CommitAll("base");
        FilePath trace = new FilePath(AppContext.BaseDirectory).Combine("TestData", "claude", "projects", "D--fake-proj", Session + ".jsonl");
        var recent = new RecentProjects(Dir.Combine("history.txt"));

        return HeadlessHost.Run(() =>
        {
            var window = new MainWindow([new FixedTraceAdapter(trace)], recent) { Width = 1400, Height = 850 };
            window.Show();
            window.OpenProject(root);
            Pump("the tree", () => window.Tree.VisibleNodes().Any(n => n.Node.Name == "api"));
            // The baseline is taken when the repositories are located and that time cannot be observed from outside: the files are rewritten with different content
            // until both changes appear (a change before the baseline does not count, one after it does)
            int version = 0;
            Pump("the changes of both repositories in the Inspector", () =>
            {
                version++;
                WriteText(api.Dir.Combine("Program.cs").Value, "class Api { int x" + version + "; }\n");
                WriteText(web.Dir.Combine("index.html").Value, "<h1>goodbye " + version + "</h1>\n");
                return window.Diff.Items.Any(e => e.Change.Path.FileName == "Program.cs") && window.Diff.Items.Any(e => e.Change.Path.FileName == "index.html");
            }, 25_000);
            WriteText(api.Dir.Combine("Program.cs").Value, "class Api { int x; }\n");
            WriteText(web.Dir.Combine("index.html").Value, "<h1>goodbye</h1>\n");

            // Each diff comes from its repository
            window.Diff.Select(web.Dir.Combine("index.html"));
            Pump("the diff of the web repository", () => window.Diff.Viewer.Text.Contains("+<h1>goodbye</h1>", StringComparison.Ordinal), 20_000);
            window.Diff.Select(api.Dir.Combine("Program.cs"));
            Pump("the diff of the api repository", () => window.Diff.Viewer.Text.Contains("+class Api { int x; }", StringComparison.Ordinal), 20_000);
            window.SwitchTab(MainWindow.DiffTab);
            Dispatcher.UIThread.RunJobs();
            Save(window, "main-multirepo");
            window.Close();
        });
    }

    [Fact]
    public Task CtrlKOpensTheSearchAndAResultJumpsToItsStepInTheGraph()
    {
        Assert.SkipUnless(GitFixture.GitAvailable(), "This test needs git installed");
        GitFixture repo = GitFixture.Init(Dir.Combine("project"));
        repo.Write("README.md", "# Project\n");
        repo.CommitAll("base");
        FilePath trace = new FilePath(AppContext.BaseDirectory).Combine("TestData", "claude", "projects", "D--fake-proj", Session + ".jsonl");
        var recent = new RecentProjects(Dir.Combine("history.txt"));

        return HeadlessHost.Run(() =>
        {
            var window = new MainWindow([new FixedTraceAdapter(trace)], recent) { Width = 1400, Height = 850 };
            window.Show();
            window.OpenProject(repo.Dir);
            Pump("the graph", () => window.Graph.Canvas.Graph.StepCount > 0);

            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            Assert.NotNull(window.SearchWindow);
            SearchWindow search = window.SearchWindow!;
            search.QueryText = "BOOM";
            Assert.Equal(SearchKind.Error, Assert.Single(search.Hits).Entry.Kind);
            Assert.StartsWith("1 ", search.Summary, StringComparison.Ordinal);
            Save(search, "search-window");

            // The result leads to its step: the graph is shown with that step selected
            window.SwitchTab(MainWindow.TelemetryTab);
            search.JumpToSelected(); // no selection: does nothing
            Assert.Equal(MainWindow.TelemetryTab, window.Tabs.SelectedIndex);
            window.JumpToFirstSearchResult();
            Assert.Equal(MainWindow.GraphTab, window.Tabs.SelectedIndex);
            Assert.True(window.Graph.DetailVisible);

            search.QueryText = "nothing of this appears in the trace";
            Assert.Empty(search.Hits);
            Assert.False(string.IsNullOrWhiteSpace(search.Summary));
            Assert.False(search.Summary.StartsWith("1 ", StringComparison.Ordinal));

            search.Close();
            Assert.Null(window.SearchWindow);
            window.Close();
        });
    }

    [Fact]
    public Task TheWindowOpensWithTheDarkThemeTheTitleAndSixTabs() => HeadlessHost.Run(() =>
    {
        var window = new MainWindow([], new RecentProjects(Dir.Combine("vacio.txt")));
        window.Show();
        Assert.Equal(MainWindow.AppTitle, window.Title);
        Assert.Equal(6, window.Tabs.ItemCount);
        Assert.Equal(MainWindow.GraphTab, window.Tabs.SelectedIndex);
        window.Close();
    });

    [Fact]
    public Task TheTabHeadersAndTheTopBarComeFromTheResources() => HeadlessHost.Run(() =>
    {
        var window = new MainWindow([], new RecentProjects(Dir.Combine("empty.txt")));
        window.Show();
        string?[] headers = [.. window.Tabs.Items.OfType<TabItem>().Select(t => t.Header as string)];
        Assert.Equal(
            [ShellStrings.TabExecutionGraph, ShellStrings.TabDiffInspector, ShellStrings.TabProcessTelemetry, ShellStrings.TabFile, ShellStrings.TabKnowledge, ShellStrings.TabHeatmap],
            headers);
        Assert.Equal(["Execution graph", "Diff inspector", "Process telemetry", "File", "Knowledge", "Heatmap"], headers);
        Assert.Equal("Ready: open a project to start", window.StatusBar.Message);
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "No project open");
        window.Close();
    });

    [Fact]
    public Task TheWindowTextsAreSpanishWhenTheUiThreadRunsInSpanish() => HeadlessHost.Run(() =>
    {
        using var _ = CultureScope.Spanish();
        var window = new MainWindow([], new RecentProjects(Dir.Combine("empty.txt")));
        window.Show();
        string?[] headers = [.. window.Tabs.Items.OfType<TabItem>().Select(t => t.Header as string)];
        Assert.Equal(["Grafo de Ejecución", "Inspector de Diffs", "Telemetría de Procesos", "Archivo", "Conocimiento", "Mapa de calor"], headers); // lang-ok
        Assert.Equal("Listo: abre un proyecto para empezar", window.StatusBar.Message); // lang-ok
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Ningún proyecto abierto"); // lang-ok
        window.Close();
    });
}
