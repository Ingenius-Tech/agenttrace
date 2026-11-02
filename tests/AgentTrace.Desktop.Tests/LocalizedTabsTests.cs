using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AgentTrace.Core;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Resources;
using AgentTrace.Testing;
using static AgentTrace.Desktop.Tests.MainWindowTests;

namespace AgentTrace.Desktop.Tests;

/// <summary>The six tabs of an observed project, captured in English and in Spanish (<c>captures/&lt;tab&gt;-&lt;lang&gt;.png</c>), with their headers coming from the resources.</summary>
public sealed class LocalizedTabsTests : TempDirTestBase
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    private static readonly string[] TabNames = ["graph", "diff", "telemetry", "file", "knowledge", "heatmap"];

    private static string[] ExpectedHeaders() =>
        [ShellStrings.TabExecutionGraph, ShellStrings.TabDiffInspector, ShellStrings.TabProcessTelemetry, ShellStrings.TabFile, ShellStrings.TabKnowledge, ShellStrings.TabHeatmap];

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("es-ES", "es")]
    public Task EachTabIsCapturedInTheLanguageOfTheUiThread(string culture, string lang)
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
            using var _ = new CultureScope(culture);
            var window = new MainWindow([new FixedTraceAdapter(trace)], recent) { Width = 1400, Height = 850 };
            window.Show();
            window.OpenProject(repo.Dir);

            string[] headers = [.. window.Tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!)];
            Assert.Equal(ExpectedHeaders(), headers);

            Pump("events in the telemetry", () => window.Telemetry.Items.Count > 5);
            Pump("the graph", () => window.Graph.Canvas.Graph.StepCount > 0);
            File.WriteAllText(repo.Dir.Combine("README.md").Value, "# Project\nnew line\n"); // a change for the Diff inspector, as the application sees it live
            Pump("the change in the Inspector", () => window.Diff.Items.Any(e => e.Change.Path.FileName == "README.md"), 20_000);
            window.Diff.Select(repo.Dir.Combine("README.md"));
            Pump("the diff", () => window.Diff.Viewer.Text.Contains("+new line", StringComparison.Ordinal), 20_000);
            Pump("the tree", () => window.Tree.VisibleNodes().Any(n => n.Node.Name == "README.md"));
            window.OpenFile("README.md");
            Pump("its content", () => window.FileView.Viewer.Text.Contains("# Project", StringComparison.Ordinal));
            window.SwitchTab(MainWindow.HeatmapTab);
            Pump("the heatmap", () => window.Heatmap.Canvas.Tree?.Find(repo.Dir.Combine("README.md")) is not null, 20_000);

            string dir = Path.Combine(AppContext.BaseDirectory, "captures");
            Directory.CreateDirectory(dir);
            for (int tab = 0; tab < TabNames.Length; tab++)
            {
                window.SwitchTab(tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(dir, TabNames[tab] + "-" + lang + ".png"), new PngBitmapEncoderOptions());
            }
            window.Close();
        });
    }

    [Fact]
    public void TheTabHeadersDifferBetweenEnglishAndSpanish()
    {
        string[] english;
        string[] spanish;
        using (CultureScope.English()) english = ExpectedHeaders();
        using (CultureScope.Spanish()) spanish = ExpectedHeaders();
        for (int i = 0; i < english.Length; i++) Assert.NotEqual(english[i], spanish[i]);
    }
}
