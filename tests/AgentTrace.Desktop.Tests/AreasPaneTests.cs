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
using AgentTrace.Desktop.Controls;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Desktop.Tests;

/// <summary>The Areas map with a synthetic session of three tasks, in a headless window with real Skia.</summary>
public sealed class AreasPaneTests : TempDirTestBase
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\project" : "/project");

    private static FilePath F(string relative) => Root.Combine(relative.Split('/'));

    private static EventHeader H(int second) => new(new SessionId("s"), "test", null, T0.AddSeconds(second), new SourceRef(new FilePath("t"), 1));

    private static IEnumerable<AgentEvent> Call(int second, string id, params AgentEvent[] effects) =>
        [new ToolCall(H(second), id, "T", ToolKind.Other, id, "{}"), .. effects];

    /// <summary>Task 1 reads the controller and changes data then the API, task 2 changes a test, task 3 the docs.</summary>
    private static FlowGraph Session()
    {
        var b = new FlowBuilder();
        IEnumerable<AgentEvent> events =
        [
            new UserMessage(H(0), "add the orders endpoint"),
            .. Call(1, "c1", new FileRead(H(2), F("src/Api/OrdersController.cs"), "c1")),
            .. Call(3, "c2", new FileChanged(H(4), F("src/Data/OrderRepository.cs"), ChangeKind.Modified, "c2")),
            .. Call(5, "c3", new FileChanged(H(6), F("src/Api/OrdersController.cs"), ChangeKind.Modified, "c3")),
            new TurnEnded(H(7), null),
            new UserMessage(H(10), "cover it with a test"),
            .. Call(11, "c4", new FileChanged(H(12), F("tests/OrdersTests.cs"), ChangeKind.Created, "c4")),
            new TurnEnded(H(13), null),
            new UserMessage(H(20), "document it"),
            .. Call(21, "c5", new FileChanged(H(22), F("README.md"), ChangeKind.Modified, "c5")),
            new TurnEnded(H(23), null),
        ];
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }

    private static AreaRule Rule(string id, string name, AreaDimension dimension, params string[] include) =>
        new(id, name, dimension, include.ToValueList(), ValueList<string>.Empty);

    private static AreaRepoSet RulesFileRepo()
    {
        var rules = new AreaRules(new[]
        {
            Rule("data", "Data", AreaDimension.Layer, "src/Data/**"),
            Rule("api", "API", AreaDimension.Layer, "src/Api/**"),
            Rule("orders", "Orders", AreaDimension.Module, "**/Orders*"),
            Rule("test", "Tests", AreaDimension.Kind, "tests/**"),
            Rule("docs", "Docs", AreaDimension.Kind, "*.md"),
        }.ToValueList());
        return new AreaRepoSet([new AreaRepo(Root, "project", rules, new AreaClassifier(Root, rules), AreaSource.File, ValueList<AreaRuleError>.Empty)]);
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

    private static (AreasPane Pane, Window Window, List<FilePath> Opened, List<FilePath> Diffed) Open(AreasSnapshot snapshot)
    {
        List<FilePath> opened = [];
        List<FilePath> diffed = [];
        var pane = new AreasPane(opened.Add, diffed.Add);
        var window = new Window { Width = 1200, Height = 650, Content = pane };
        window.Show();
        pane.SetSnapshot(snapshot);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (pane, window, opened, diffed);
    }

    private static Point At(AreasPane pane, Window window, Point inCanvas) => pane.Canvas.TranslatePoint(inCanvas, window)!.Value;

    private static void Click(Window window, Point p)
    {
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
    }

    private static string[] RowNames(AreasPane pane) => [.. pane.Canvas.Matrix.Rows.Select(r => r.Name)];

    [Fact]
    public Task TheRowsFollowTheRulesFileAndEachDimensionRegroupsTheSameActivity() => HeadlessHost.Run(() =>
    {
        (AreasPane pane, Window window, _, _) = Open(AreasSnapshot.Of(Session(), [], RulesFileRepo(), Root));

        // A rules file with layers: the layers come first, in the order of the file, and what matches none goes to "Unclassified"
        Assert.Equal(AreaDimension.Layer, pane.Dimension);
        Assert.Equal(["Data", "API", "Unclassified"], RowNames(pane));
        Assert.Equal([0, 1, 2], pane.Canvas.Matrix.Columns.Select(c => c.Turn).ToArray());
        Assert.Equal(3, pane.Canvas.Matrix.Bands.Count);
        Save(window, "areas-layers");

        pane.SetDimension(AreaDimension.Kind);
        Assert.Equal(["Tests", "Docs", "Unclassified"], RowNames(pane));
        pane.SetDimension(AreaDimension.Module);
        Assert.Equal(["Orders", "Unclassified"], RowNames(pane));
        Save(window, "areas-modules");

        // Only writes: the reads are left out of the rows
        pane.SetDimension(AreaDimension.Layer);
        pane.SetFilter(new ActivityFilter(false, true, false));
        Assert.Equal(new ActivityFilter(false, true, false), pane.Filter);
        Assert.All(pane.Canvas.Matrix.Rows, r => Assert.Equal(r.Total.Writes, r.Total.Value(pane.Filter)));
        window.Close();
    });

    [Fact]
    public Task AClickOnACellListsItsFilesAndAnActionOpensThem() => HeadlessHost.Run(() =>
    {
        (AreasPane pane, Window window, List<FilePath> opened, List<FilePath> diffed) = Open(AreasSnapshot.Of(Session(), [], RulesFileRepo(), Root));
        Assert.False(pane.PanelVisible);

        // The cell of the API layer in the first turn: a read and a change of the same controller
        Click(window, At(pane, window, pane.Canvas.CenterOf(1, 0)));
        Assert.Equal((1, 0), pane.Canvas.SelectedCell);
        Assert.True(pane.PanelVisible);
        Assert.Contains(pane.PanelTexts, t => t.Contains("OrdersController.cs", StringComparison.Ordinal));
        Assert.Contains(pane.PanelTexts, t => t.Contains("API", StringComparison.Ordinal) && t.Contains('1'));
        Save(window, "areas-cell");

        pane.OpenPanelRow(0);
        Assert.Equal([F("src/Api/OrdersController.cs")], opened);
        Assert.Empty(diffed);

        // A click on a cell without activity clears the selection and closes the panel
        Click(window, At(pane, window, pane.Canvas.CenterOf(0, 1)));
        Assert.False(pane.PanelVisible);
        window.Close();
    });

    [Fact]
    public Task ADoubleClickOnARowListsItsFilesAndTheMoreLineAppearsWhenTheyDoNotFit() => HeadlessHost.Run(() =>
    {
        (AreasPane pane, Window window, List<FilePath> opened, _) = Open(AreasSnapshot.Of(Session(), [], RulesFileRepo(), Root));
        Point header = At(pane, window, pane.Canvas.CenterOfRowHeader(1));
        Click(window, header);
        Click(window, header);
        Assert.True(pane.Canvas.IsExpanded(pane.Canvas.Matrix.Rows[1].Key));
        Assert.Equal("src/Api/OrdersController.cs".Replace('/', Path.DirectorySeparatorChar), pane.Canvas.Lines[2].Label.Replace('/', Path.DirectorySeparatorChar));
        Save(window, "areas-expanded");

        // A double click on the row of a file opens it
        Point file = At(pane, window, pane.Canvas.CenterOfRowHeader(2));
        Click(window, file);
        Click(window, file);
        Assert.Equal([F("src/Api/OrdersController.cs")], opened);
        window.Close();
    });

    [Fact]
    public Task SelectingATaskDimsTheOthersAndTracesHowTheChangeSpread() => HeadlessHost.Run(() =>
    {
        (AreasPane pane, Window window, _, _) = Open(AreasSnapshot.Of(Session(), [], RulesFileRepo(), Root));
        Click(window, At(pane, window, pane.Canvas.CenterOfTaskHeader(0)));
        Assert.Equal(0, pane.Canvas.SelectedTask);

        // Data was changed first and then the API: two points, in that order
        IReadOnlyList<(int Row, int Column)> path = pane.Canvas.Matrix.Propagation(0);
        Assert.Equal([(0, 0), (1, 0)], path);
        // The header summarizes the task and flags a change with no test (the rules define a "test" kind); the task that only wrote a test has no badge
        IReadOnlyList<AreaTaskSummary> summaries = pane.Canvas.Matrix.Snapshot.Summaries;
        Assert.Equal(AreaBadges.UntestedChange, summaries[0].Badges);
        Assert.Equal(["Data", "API"], summaries[0].LayerOrder.Select(r => pane.Canvas.Matrix.Snapshot.NameOf(r)).ToArray());
        Assert.Equal(AreaBadges.None, summaries[1].Badges);
        Assert.Equal(AreaBadges.UntestedChange, summaries[2].Badges);
        Save(window, "areas-task");

        // The same click again deselects, as Escape does
        Click(window, At(pane, window, pane.Canvas.CenterOfTaskHeader(0)));
        Assert.Null(pane.Canvas.SelectedTask);
        window.Close();
    });

    [Fact]
    public Task TheStartupSelectionsApplyAsSoonAsTheyExist() => HeadlessHost.Run(() =>
    {
        var pane = new AreasPane(_ => { }, _ => { });
        var window = new Window { Width = 1200, Height = 650, Content = pane };
        window.Show();
        pane.SetPending("task:1", "api");
        Assert.Null(pane.Canvas.SelectedTask); // there is no data yet
        pane.SetSnapshot(AreasSnapshot.Of(Session(), [], RulesFileRepo(), Root));
        Assert.Equal(1, pane.Canvas.SelectedTask);
        Assert.True(pane.Canvas.IsExpanded(pane.Canvas.Matrix.Rows[1].Key));

        pane.SetPending("data", null);
        Assert.Equal(0, pane.Canvas.SelectedRow);
        pane.SetPending("turn:2", null);
        Assert.Equal(2, pane.Canvas.SelectedTurn);
        window.Close();
    });

    [Fact]
    public Task WithoutARulesFileTheSuggestedModulesFillTheRows()
    {
        FilePath project = Dir.Combine("shop");
        Directory.CreateDirectory(project.Combine("src", "Api").Value);
        Directory.CreateDirectory(project.Combine("src", "Core").Value);
        File.WriteAllText(project.Combine("src", "Api", "Api.csproj").Value, "<Project />");
        File.WriteAllText(project.Combine("src", "Api", "Controller.cs").Value, "class C {}");
        File.WriteAllText(project.Combine("src", "Core", "package.json").Value, "{}");
        File.WriteAllText(project.Combine("src", "Core", "Orders.cs").Value, "class O {}");
        File.WriteAllText(project.Combine("README.md").Value, "# shop");
        ProjectListing listing = ProjectScanner.Scan(project, IgnoreRulesNone(project), FileStatusIndex.Empty(project), 1000);
        AreaRepoSet repos = AreaRepoSet.Load([project], listing);
        Assert.Equal(AreaSource.Suggested, repos.Repos.Single().Source);

        var b = new FlowBuilder();
        b.Accept(new UserMessage(H(0), "work"));
        foreach (AgentEvent e in Call(1, "c1", new FileChanged(H(2), project.Combine("src", "Api", "Controller.cs"), ChangeKind.Modified, "c1"))) b.Accept(e);
        foreach (AgentEvent e in Call(3, "c2", new FileRead(H(4), project.Combine("src", "Core", "Orders.cs"), "c2"))) b.Accept(e);
        foreach (AgentEvent e in Call(5, "c3", new FileChanged(H(6), project.Combine("README.md"), ChangeKind.Modified, "c3"))) b.Accept(e);

        return HeadlessHost.Run(() =>
        {
            (AreasPane pane, Window window, _, _) = Open(AreasSnapshot.Of(b.Snapshot(), [], repos, project));
            Assert.Equal(AreaDimension.Module, pane.Dimension); // no layers: modules first
            Assert.Equal(["shop", "Api", "Core"], RowNames(pane));
            Save(window, "areas-suggested");
            window.Close();
        });
    }

    private static AgentTrace.Ingest.IgnoreRules IgnoreRulesNone(FilePath project) => AgentTrace.Ingest.IgnoreRules.WithoutGit(project);
}
