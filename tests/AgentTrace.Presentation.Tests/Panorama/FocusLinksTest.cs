using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Panorama;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Panorama;

/// <summary>How a selection in one pane maps to turns, steps, files and Areas rows (the neutral model, no window).</summary>
public sealed class FocusLinksTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\proj" : "/proj");

    private static FilePath F(string relative) => Root.Combine(relative.Split('/'));

    private static EventHeader H(int second) => new(new SessionId("s"), "test", null, T0.AddSeconds(second), new SourceRef(new FilePath("t"), 1));

    /// <summary>
    /// Turn 0 (task 0): c1 reads app/a.cs, c2 writes data/d.cs. Turn 1 (task 1): c3 reads app/a.cs, c4 runs a command that touches nothing the
    /// trace knows. Turn 2 (continuation, task 1): c5 writes README.md.
    /// </summary>
    private static FlowGraph Flow()
    {
        var b = new FlowBuilder();
        b.Accept(new UserMessage(H(0), "one"));
        b.Accept(new ToolCall(H(1), "c1", "Read", ToolKind.Read, "c1", "{}"));
        b.Accept(new FileRead(H(1), F("src/App/a.cs"), "c1"));
        b.Accept(new ToolCall(H(2), "c2", "Write", ToolKind.Write, "c2", "{}"));
        b.Accept(new FileChanged(H(2), F("src/Data/d.cs"), ChangeKind.Modified, "c2"));
        b.Accept(new UserMessage(H(10), "two"));
        b.Accept(new ToolCall(H(11), "c3", "Read", ToolKind.Read, "c3", "{}"));
        b.Accept(new FileRead(H(11), F("src/App/a.cs"), "c3"));
        b.Accept(new ToolCall(H(12), "c4", "Bash", ToolKind.Execute, "c4", "{}"));
        b.Accept(new TurnEnded(H(13), null));
        b.Accept(new ToolCall(H(20), "c5", "Write", ToolKind.Write, "c5", "{}"));
        b.Accept(new FileChanged(H(20), F("README.md"), ChangeKind.Modified, "c5"));
        return b.Snapshot();
    }

    private static AreaFileActivity Act(string path, int reads = 0, int writes = 0, int order = 0) => new(F(path), reads, 0 + writes, 0, 0, order);

    private static AreaRule Rule(string id, AreaDimension dimension, params string[] include) =>
        new(id, char.ToUpperInvariant(id[0]) + id[1..], dimension, include.ToValueList(), ValueList<string>.Empty);

    private static AreaRepo Repo(FilePath root, params AreaRule[] rules)
    {
        var set = new AreaRules(rules.ToValueList());
        return new AreaRepo(root, root.FileName, set, new AreaClassifier(root, set), AreaSource.File, ValueList<AreaRuleError>.Empty);
    }

    private static readonly AreaRepo Layered = Repo(Root, Rule("data", AreaDimension.Layer, "src/Data/**"), Rule("app", AreaDimension.Layer, "src/App/**"));

    private static AreasMatrix Matrix(FlowGraph flow) => AreasMatrix.Build(
        new AreasSnapshot(flow, new AreaActivity(new[]
        {
            new AreaTurnActivity(0, new[] { Act("src/App/a.cs", reads: 1, order: 0), Act("src/Data/d.cs", writes: 1, order: 1) }.ToValueList()),
            new AreaTurnActivity(1, new[] { Act("src/App/a.cs", reads: 1) }.ToValueList()),
            new AreaTurnActivity(2, new[] { Act("README.md", writes: 1) }.ToValueList()),
        }.ToValueList()), new AreaRepoSet([Layered]), Root), AreaDimension.Layer, ActivityFilter.All);

    private static ChangeEntry Git(string file, string call, Confidence confidence = Confidence.Exact) =>
        new(new FileChange(F(file), ChangeKind.Modified, null),
            new Attribution(confidence, new[] { new ToolCall(H(12), call, "Bash", ToolKind.Execute, "", "{}") }.ToValueList()));

    private static FocusTargets Of(TraceFocus focus, params ChangeEntry[] changes)
    {
        FlowGraph flow = Flow();
        return FocusLinks.Of(focus, flow, changes, Matrix(flow));
    }

    [Fact]
    public void AStepLinksToItsTurnItsFilesAndTheRowsOfThoseFiles()
    {
        FocusTargets t = Of(TraceFocus.ForStep("c2"));
        Assert.Equal((0, 0), (t.FirstTurn, t.LastTurn));
        Assert.Equal(["c2"], t.CallIds);
        Assert.Equal([F("src/Data/d.cs")], t.Files);
        Assert.Equal(["data"], t.AreaRows.Select(r => r.AreaId).ToArray());
    }

    [Fact]
    public void AFileTheGitChangeAttributesToTheCallCountsAsAFileOfTheStep()
    {
        FocusTargets t = Of(TraceFocus.ForStep("c4"), Git("src/Data/d.cs", "c4"), Git("README.md", "c4", Confidence.Ambiguous));
        Assert.Equal([F("src/Data/d.cs")], t.Files);
        Assert.Equal((1, 1), (t.FirstTurn, t.LastTurn));
    }

    [Fact]
    public void AStepWithoutFilesLinksOnlyToItsTurn()
    {
        FocusTargets t = Of(TraceFocus.ForStep("c4"));
        Assert.Equal(["c4"], t.CallIds);
        Assert.Empty(t.Files);
        Assert.Empty(t.AreaRows);
        Assert.Equal(1, t.FirstTurn);
    }

    [Fact]
    public void ATurnLinksToAllItsStepsAndTheUnionOfTheirFiles()
    {
        FocusTargets t = Of(TraceFocus.ForGraphTurn(0));
        Assert.True(new HashSet<string> { "c1", "c2" }.SetEquals(t.CallIds));
        Assert.True(new HashSet<FilePath> { F("src/App/a.cs"), F("src/Data/d.cs") }.SetEquals(t.Files));
        Assert.Equal(2, t.AreaRows.Count);
    }

    [Fact]
    public void ATaskLinksToTheTurnRangeAndEveryStepAndFileInIt()
    {
        FocusTargets t = Of(TraceFocus.ForTask(1));
        Assert.Equal((1, 2), (t.FirstTurn, t.LastTurn));
        Assert.True(new HashSet<string> { "c3", "c4", "c5" }.SetEquals(t.CallIds));
        Assert.True(new HashSet<FilePath> { F("src/App/a.cs"), F("README.md") }.SetEquals(t.Files));
        Assert.Contains(t.AreaRows, r => r.AreaId is null); // README.md is in the Unclassified row
    }

    [Fact]
    public void ACellLinksToItsTurnItsFilesAndTheStepsOfThatTurnThatTouchedThem()
    {
        FlowGraph flow = Flow();
        AreasMatrix matrix = Matrix(flow);
        AreaRowKey app = matrix.Rows.First(r => r.Key.AreaId == "app").Key;
        FocusTargets t = FocusLinks.Of(TraceFocus.ForCell(app, 1), flow, [], matrix);
        Assert.Equal((1, 1), (t.FirstTurn, t.LastTurn));
        Assert.Equal([F("src/App/a.cs")], t.Files);
        Assert.Equal(["c3"], t.CallIds); // c1 touched the same file but in turn 0; c4 touched nothing
        Assert.Equal([app], t.AreaRows);
    }

    [Fact]
    public void AFileTouchedInSeveralTurnsLinksToAllOfThem()
    {
        FocusTargets t = Of(TraceFocus.ForFile(F("src/App/a.cs")));
        Assert.True(new HashSet<string> { "c1", "c3" }.SetEquals(t.CallIds));
        Assert.Equal((0, 1), (t.FirstTurn, t.LastTurn));
        Assert.Equal([F("src/App/a.cs")], t.Files);
        Assert.Single(t.AreaRows);
    }

    [Fact]
    public void UnknownTargetsResolveToNothingToHighlight()
    {
        Assert.True(Of(TraceFocus.ForStep("nope")).CallIds.Count == 0);
        Assert.True(Of(TraceFocus.ForGraphTurn(99)).IsEmpty);
        Assert.True(Of(TraceFocus.ForTask(99)).IsEmpty);
        Assert.True(Of(TraceFocus.ForFile(F("not/there.cs"))).CallIds.Count == 0);
        Assert.True(Of(TraceFocus.ForAreasTurn(-1)).IsEmpty);
    }

    [Fact]
    public void RowsOfAndFilesOfFollowTheMatrixDimensionAndIgnoreFilesWithoutARow()
    {
        AreasMatrix matrix = Matrix(Flow());
        Assert.Equal(2, matrix.RowsOf([F("src/App/a.cs"), F("README.md"), F("src/App/other.cs")]).Count);
        Assert.Equal("data", Assert.Single(matrix.RowsOf([F("src/Data/never-touched.cs")])).AreaId); // classified like the files with activity
        Assert.Empty(matrix.RowsOf([]));
        Assert.Empty(matrix.FilesOf(matrix.Rows[0].Key, 2));
        Assert.Empty(matrix.FilesOf(new AreaRowKey(Root, "ghost"), 0));
    }
}
