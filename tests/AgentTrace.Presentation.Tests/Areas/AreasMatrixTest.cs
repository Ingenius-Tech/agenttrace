using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Areas;

public sealed class AreasMatrixTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\proj" : "/proj");

    private static FilePath F(string relative) => Root.Combine(relative.Split('/'));

    private static EventHeader H(int second) => new(new SessionId("s"), "test", null, T0.AddSeconds(second), new SourceRef(new FilePath("t"), 1));

    /// <summary>Three turns: two prompts (tasks 0 and 1) and a continuation of the second one.</summary>
    private static FlowGraph Flow()
    {
        var b = new FlowBuilder();
        b.Accept(new UserMessage(H(0), "one"));
        b.Accept(new UserMessage(H(10), "two"));
        b.Accept(new ToolCall(H(11), "c", "T", ToolKind.Other, "c", "{}"));
        b.Accept(new TurnEnded(H(12), null));
        b.Accept(new ToolCall(H(20), "d", "T", ToolKind.Other, "d", "{}"));
        return b.Snapshot();
    }

    private static AreaFileActivity Act(string path, int reads = 0, int writes = 0, int external = 0, int errors = 0, int order = 0) =>
        new(F(path), reads, writes, external, errors, order);

    private static AreaRule Rule(string id, AreaDimension dimension, params string[] include) =>
        new(id, char.ToUpperInvariant(id[0]) + id[1..], dimension, include.ToValueList(), ValueList<string>.Empty);

    private static AreaRepo Repo(FilePath root, AreaSource source, params AreaRule[] rules)
    {
        var set = new AreaRules(rules.ToValueList());
        return new AreaRepo(root, root.FileName, set, new AreaClassifier(root, set), source, ValueList<AreaRuleError>.Empty);
    }

    private static readonly AreaRepo LayeredRepo = Repo(Root, AreaSource.File,
        Rule("data", AreaDimension.Layer, "src/Data/**"), Rule("app", AreaDimension.Layer, "src/App/**"),
        Rule("core", AreaDimension.Module, "src/**"), Rule("test", AreaDimension.Kind, "tests/**"));

    private static AreasSnapshot Snapshot(AreaRepoSet repos, params AreaTurnActivity[] turns) =>
        new(Flow(), new AreaActivity(turns.ToValueList()), repos, Root);

    private static AreaTurnActivity Turn(int turn, params AreaFileActivity[] files) => new(turn, files.ToValueList());

    [Fact]
    public void ActivityInATurnNoTaskCoversMakesNoRowsAndTheMatrixIsEmpty()
    {
        // No turns in the flow: git-only changes land in turn 0 with no task, so there is no column to draw them in
        var s = new AreasSnapshot(new FlowBuilder().Snapshot(), new AreaActivity(new[] { Turn(0, Act("src/App/a.cs", writes: 1)) }
            .ToValueList()), new AreaRepoSet([LayeredRepo]), Root);
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        Assert.Empty(m.Columns);
        Assert.Empty(m.Rows);
        Assert.True(m.IsEmpty);
    }

    [Fact]
    public void RowsFollowTheRulesOrderAndUnclassifiedIsLast()
    {
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]),
            Turn(0, Act("src/App/a.cs", writes: 1), Act("README.md", reads: 1)),
            Turn(1, Act("src/Data/d.cs", writes: 1)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        Assert.Equal(["Data", "App", "Unclassified"], m.Rows.Select(r => r.Name).ToArray());
        Assert.Null(m.Rows[2].Key.AreaId);
    }

    [Fact]
    public void ColumnsAreTheTurnsWithActivityGroupedIntoTasks()
    {
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]),
            Turn(0, Act("src/App/a.cs", writes: 1)),
            Turn(2, Act("src/Data/d.cs", reads: 1)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        Assert.Equal([0, 2], m.Columns.Select(c => c.Turn).ToArray());
        Assert.Equal([0, 1], m.Bands.Select(b => b.Task.Index).ToArray());
        Assert.Equal(1, m.Bands[1].Count);
        Assert.Equal(1, m.Columns[1].Band);
        Assert.Equal(1, m.ColumnOfTurn(2));
        Assert.Equal(-1, m.ColumnOfTurn(1));
    }

    [Fact]
    public void CellsSumTheFilesOfAnAreaInATurn()
    {
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]),
            Turn(0, Act("src/App/a.cs", reads: 2, writes: 1, order: 1), Act("src/App/b.cs", errors: 1, order: 0)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        AreaCell cell = m.Cell(0, 0)!;
        Assert.Equal(2, cell.Activity.Reads);
        Assert.Equal(1, cell.Activity.Writes);
        Assert.Equal(1, cell.Activity.Errors);
        Assert.Equal([F("src/App/b.cs"), F("src/App/a.cs")], cell.Files.Select(f => f.Path).ToArray()); // first touch first
        Assert.Null(m.Cell(0, 1));
    }

    [Fact]
    public void RegroupingByAnotherDimensionClassifiesTheSameFilesAgain()
    {
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]),
            Turn(0, Act("src/App/a.cs", writes: 1), Act("tests/AppTest.cs", writes: 1)));
        Assert.Equal(["App", "Unclassified"], AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All).Rows.Select(r => r.Name).ToArray());
        Assert.Equal(["Core", "Unclassified"], AreasMatrix.Build(s, AreaDimension.Module, ActivityFilter.All).Rows.Select(r => r.Name).ToArray());
        Assert.Equal(["Test", "Unclassified"], AreasMatrix.Build(s, AreaDimension.Kind, ActivityFilter.All).Rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void TheFilterHidesWhatItExcludesAlongWithTheTurnsThatBecomeEmpty()
    {
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]),
            Turn(0, Act("src/App/a.cs", reads: 3)),
            Turn(2, Act("src/App/a.cs", writes: 1)));
        AreasMatrix writes = AreasMatrix.Build(s, AreaDimension.Layer, new ActivityFilter(false, true, false));
        Assert.Equal([2], writes.Columns.Select(c => c.Turn).ToArray());
        Assert.Equal(1, writes.Rows.Single().Total.Writes);
        Assert.Equal(0, writes.Rows.Single().Total.Reads);
        Assert.True(AreasMatrix.Build(s, AreaDimension.Layer, new ActivityFilter(false, false, true)).IsEmpty);
    }

    [Fact]
    public void ARepositoryWithoutRulesGetsSuggestedModulesByIdAndTheRootModule()
    {
        var suggested = new AreaRules(new[]
        {
            new AreaRule("src/Api", "Api", AreaDimension.Module, ["src/Api/**"], []),
            new AreaRule(AutoModules.RootId, "proj", AreaDimension.Module, ["**"], []),
        }.ToValueList());
        var repo = new AreaRepo(Root, "proj", suggested, new AreaClassifier(Root, suggested), AreaSource.Suggested, ValueList<AreaRuleError>.Empty);
        AreasSnapshot s = Snapshot(new AreaRepoSet([repo]), Turn(0, Act("src/Api/x.cs", writes: 1), Act("build.ps1", reads: 1)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Module, ActivityFilter.All);
        Assert.Equal(["proj", "Api"], m.Rows.Select(r => r.Name).ToArray()); // "." sorts before "src/Api"
    }

    [Fact]
    public void WithSeveralRepositoriesTheRowKeyIncludesTheRepositoryAndTheNameIsPrefixed()
    {
        FilePath other = Root.Combine("sub");
        AreaRepo sub = Repo(other, AreaSource.File, Rule("core", AreaDimension.Module, "**"));
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo, sub]),
            Turn(0, Act("src/App/a.cs", writes: 1), Act("sub/x.cs", writes: 1)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Module, ActivityFilter.All);
        Assert.Equal(2, m.Rows.Count);
        Assert.Equal(new AreaRowKey(Root, "core"), m.Rows[0].Key);
        Assert.Equal(new AreaRowKey(other, "core"), m.Rows[1].Key); // the deepest root classifies sub/x.cs
        Assert.Equal(["proj / Core", "sub / Core"], m.Rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void FilesInNoRepositoryAreUnclassified()
    {
        AreasSnapshot s = Snapshot(AreaRepoSet.Empty, Turn(0, Act("a.txt", reads: 1)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        Assert.Equal("Unclassified", m.Rows.Single().Name);
    }

    [Fact]
    public void ExpandingARowListsItsFilesByActivityWithAMoreLine()
    {
        AreaFileActivity[] many = [.. Enumerable.Range(0, AreasMatrix.FilesPerExpandedRow + 3).Select(i => Act("src/App/f" + i.ToString("00", CultureInfo.InvariantCulture) + ".cs", reads: 1, order: i))];
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]), Turn(0, [.. many, Act("src/App/hot.cs", writes: 5)]));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        Assert.Single(m.DisplayRows(new HashSet<AreaRowKey>()));
        IReadOnlyList<AreaDisplayRow> lines = m.DisplayRows(new HashSet<AreaRowKey> { m.Rows[0].Key });
        Assert.Equal(1 + AreasMatrix.FilesPerExpandedRow + 1, lines.Count);
        Assert.Equal("src/App/hot.cs", lines[1].Label); // the most active file first, relative to the project
        Assert.True(lines[^1].IsMore);
        Assert.Equal("+4 more files", lines[^1].Label);
        Assert.Equal(5, m.ActivityAt(lines[1], 0).Writes);
        Assert.Equal(5, m.ActivityAt(lines[0], 0).Writes);
    }

    [Fact]
    public void PropagationIsTheFirstChangeOfEachRowInTimeOrderEvenIfAReadCameFirst()
    {
        // Turn 0: the API file was read (order 0) and changed later (order 3); the data file was changed at order 1
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]),
            Turn(0, new AreaFileActivity(F("src/App/a.cs"), 1, 1, 0, 0, 0, 3), new AreaFileActivity(F("src/Data/d.cs"), 0, 1, 0, 0, 1, 1)),
            Turn(1, Act("src/Data/d.cs", writes: 1)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        Assert.Equal([(0, 0), (1, 0)], m.Propagation(0)); // Data (row 0) before App (row 1)
        Assert.Empty(m.Propagation(7)); // no such task
    }

    [Fact]
    public void TheHueIsTheHeaviestActiveKind()
    {
        Assert.Equal(HeatHue.Error, AreasMatrix.Hue(new FileActivity(3, 0, 3, 0, 0, 3), ActivityFilter.All));
        Assert.Equal(HeatHue.Modified, AreasMatrix.Hue(new FileActivity(1, 0, 2, 0, 0, 1), ActivityFilter.All));
        Assert.Equal(HeatHue.Read, AreasMatrix.Hue(new FileActivity(2, 0, 1, 0, 0, 0), ActivityFilter.All));
        Assert.Equal(HeatHue.Read, AreasMatrix.Hue(new FileActivity(1, 0, 9, 0, 0, 9), new ActivityFilter(true, false, false)));
        Assert.Equal(HeatHue.None, AreasMatrix.Hue(default, ActivityFilter.All));
    }

    [Fact]
    public void ThePointsOfTheMatrixAreFoundWhereTheyArePainted()
    {
        AreasSnapshot s = Snapshot(new AreaRepoSet([LayeredRepo]),
            Turn(0, Act("src/App/a.cs", writes: 1)), Turn(2, Act("src/Data/d.cs", reads: 1)));
        AreasMatrix m = AreasMatrix.Build(s, AreaDimension.Layer, ActivityFilter.All);
        double x = AreasGeometry.ColumnX(1, 0, 1) + 5;
        double y = AreasGeometry.RowY(1, 0) + 5;
        Assert.Equal(new AreasHit(AreasHitKind.Cell, 1, 1), AreasGeometry.HitTest(x, y, 0, 0, 1, 2, 2, m.Bands));
        Assert.Equal(new AreasHit(AreasHitKind.RowHeader, 1, -1), AreasGeometry.HitTest(20, y, 0, 0, 1, 2, 2, m.Bands));
        Assert.Equal(new AreasHit(AreasHitKind.TaskHeader, -1, 1), AreasGeometry.HitTest(x, 5, 0, 0, 1, 2, 2, m.Bands));
        Assert.Equal(new AreasHit(AreasHitKind.TurnHeader, -1, 1), AreasGeometry.HitTest(x, AreasGeometry.TaskBandHeight + 4, 0, 0, 1, 2, 2, m.Bands));
        Assert.Equal(AreasHit.None, AreasGeometry.HitTest(x, AreasGeometry.RowY(5, 0), 0, 0, 1, 2, 2, m.Bands));
        // Scrolling and zooming move the cells
        Assert.Equal(new AreasHit(AreasHitKind.Cell, 0, 1), AreasGeometry.HitTest(AreasGeometry.HeaderWidth + 5, AreasGeometry.TopHeight + 5, AreasGeometry.ColumnWidth, 0, 1, 2, 2, m.Bands));
    }
}
