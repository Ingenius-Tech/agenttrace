using System.Globalization;
using AgentTrace.Core.Areas;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests.Areas;

public sealed class AreaActivityBuilderTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\proj" : "/proj");

    private static FilePath F(string relative) => Root.Combine(relative.Split('/'));

    private static DateTimeOffset At(int second) => T0.AddSeconds(second);

    private static EventHeader H(int second, string? subagent = null) =>
        new(new SessionId("s"), "test", subagent, At(second), new SourceRef(new FilePath("t"), 1));

    private static ToolCall Call(int s, string id, FilePath? target = null, string? sub = null) =>
        new(H(s, sub), id, "T", ToolKind.Other, id, "{}") { Target = target };

    private static FlowGraph Flow(IEnumerable<AgentEvent> events)
    {
        var b = new FlowBuilder();
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }

    private static AreaActivity Build(FlowGraph flow, params ChangeEntry[] changes) => AreaActivityBuilder.Build(flow, changes, Root);

    private static AreaFileActivity Of(AreaActivity a, int turn, string relative) =>
        a.Turns.Single(t => t.Turn == turn).Files.Single(f => f.Path == F(relative));

    private static ChangeEntry Git(string relative, ChangeKind kind, DateTimeOffset? modified, Attribution attribution) =>
        new(new FileChange(F(relative), kind, modified), attribution);

    private static Attribution By(Confidence confidence, params ToolCall[] calls) => new(confidence, calls.ToValueList());

    private static readonly AgentEvent[] TwoTurns =
    [
        new UserMessage(H(0), "one"), Call(1, "w1"), new FileChanged(H(2), new FilePath(OperatingSystem.IsWindows() ? @"C:\proj\src\a.cs" : "/proj/src/a.cs"), ChangeKind.Modified, "w1"),
        new TurnEnded(H(3), null),
        new UserMessage(H(10), "two"), Call(11, "w2"), new TurnEnded(H(15), null),
    ];

    [Fact]
    public void AnEmptyFlowAndNoChangesHaveNoActivity()
    {
        Assert.Empty(Build(Flow([])).Turns);
    }

    [Fact]
    public void TraceOnlyCountsReadsAndWritesPerTurnAndFile()
    {
        FlowGraph flow = Flow(
        [
            new UserMessage(H(0), "one"), Call(1, "r"), Call(1, "w"),
            new FileRead(H(2), F("src/a.cs"), "r"), new FileRead(H(2), F("src/b.cs"), "r"),
            new FileChanged(H(3), F("src/a.cs"), ChangeKind.Modified, "w"),
            new UserMessage(H(10), "two"), Call(11, "w2"), new FileChanged(H(12), F("src/a.cs"), ChangeKind.Created, "w2"),
        ]);
        AreaActivity a = Build(flow);
        Assert.Equal([0, 1], a.Turns.Select(t => t.Turn).ToArray());
        Assert.Equal(new AreaFileActivity(F("src/a.cs"), 1, 1, 0, 0, 0, 1), Of(a, 0, "src/a.cs"));
        Assert.Equal(1, Of(a, 0, "src/b.cs").Reads);
        Assert.Equal(0, Of(a, 0, "src/b.cs").Writes);
        Assert.Equal(1, Of(a, 1, "src/a.cs").Writes);
        Assert.Equal(0, Of(a, 1, "src/a.cs").Reads);
    }

    [Fact]
    public void ATurnWithoutActivityIsNotListed()
    {
        FlowGraph flow = Flow([new UserMessage(H(0), "chat"), new UserMessage(H(5), "work"), Call(6, "r"), new FileRead(H(7), F("a.txt"), "r")]);
        Assert.Equal([1], Build(flow).Turns.Select(t => t.Turn).ToArray());
    }

    [Fact]
    public void GitOnlyAttributedToACallIsAnAgentWriteInThatCallsTurn()
    {
        FlowGraph flow = Flow(TwoTurns);
        ToolCall w2 = Call(11, "w2");
        AreaActivity a = Build(flow, Git("src/sh.cs", ChangeKind.Created, At(12), By(Confidence.Exact, w2)));
        Assert.Equal(new AreaFileActivity(F("src/sh.cs"), 0, 1, 0, 0, 0, 0), Of(a, 1, "src/sh.cs"));
    }

    [Theory]
    [InlineData(Confidence.Exact)]
    [InlineData(Confidence.Probable)]
    public void ExactAndProbableAttributionsUseTheirCall(Confidence confidence)
    {
        FlowGraph flow = Flow(TwoTurns);
        AreaActivity a = Build(flow, Git("x.cs", ChangeKind.Modified, At(2), By(confidence, Call(11, "w2"))));
        Assert.Equal(1, Of(a, 1, "x.cs").Writes);
        Assert.Equal(0, Of(a, 1, "x.cs").External);
    }

    [Fact]
    public void AnAmbiguousAttributionCountsOnceInTheEarliestCandidate()
    {
        FlowGraph flow = Flow(TwoTurns);
        AreaActivity a = Build(flow, Git("x.cs", ChangeKind.Modified, At(12), By(Confidence.Ambiguous, Call(11, "w2"), Call(1, "w1"))));
        Assert.Equal([0], a.Turns.Select(t => t.Turn).ToArray());
        Assert.Equal(1, Of(a, 0, "x.cs").Writes);
    }

    [Fact]
    public void AnAttributedCallThatIsNotInTheFlowFallsBackToAnExternalChange()
    {
        FlowGraph flow = Flow(TwoTurns);
        AreaActivity a = Build(flow, Git("x.cs", ChangeKind.Modified, At(12), By(Confidence.Exact, Call(12, "ghost"))));
        Assert.Equal(new AreaFileActivity(F("x.cs"), 0, 0, 1, 0, int.MaxValue, int.MaxValue), Of(a, 1, "x.cs"));
    }

    [Fact]
    public void AGitChangeWithoutACallIsExternalInTheTurnThatContainsItsTime()
    {
        FlowGraph flow = Flow(TwoTurns);
        AreaActivity a = Build(flow, Git("x.cs", ChangeKind.Modified, At(2), Attribution.None()));
        Assert.Equal(new AreaFileActivity(F("x.cs"), 0, 0, 1, 0, int.MaxValue, int.MaxValue), Of(a, 0, "x.cs"));
    }

    [Fact]
    public void AGitChangeOutsideEveryTurnOrUndatedGoesToTheLastTurn()
    {
        FlowGraph flow = Flow(TwoTurns);
        AreaActivity a = Build(flow,
            Git("late.cs", ChangeKind.Modified, At(500), Attribution.None()),
            Git("gone.cs", ChangeKind.Deleted, null, Attribution.Undated()));
        Assert.Equal(1, Of(a, 1, "late.cs").External);
        Assert.Equal(1, Of(a, 1, "gone.cs").External);
        Assert.Equal(0, Of(a, 1, "gone.cs").Writes);
    }

    [Fact]
    public void WithNoTurnsGitChangesGoToTurnZero()
    {
        AreaActivity a = Build(Flow([]), Git("x.cs", ChangeKind.Created, At(1), Attribution.None()));
        Assert.Equal(1, Of(a, 0, "x.cs").External);
    }

    [Fact]
    public void ABothSourcesChangeIsNotCountedTwice()
    {
        FlowGraph flow = Flow(TwoTurns); // the trace reports src/a.cs as modified in turn 0
        AreaActivity a = Build(flow, Git("src/a.cs", ChangeKind.Modified, At(2), By(Confidence.Exact, Call(1, "w1"))));
        Assert.Equal(1, Of(a, 0, "src/a.cs").Writes);
        Assert.Equal(0, Of(a, 0, "src/a.cs").External);
        Assert.Single(a.Turns);
        // also if the trace reported it as external
        FlowGraph external = Flow([new UserMessage(H(0), "p"), new FileChanged(H(1), F("e.txt"), ChangeKind.Modified, null)]);
        AreaActivity b = Build(external, Git("e.txt", ChangeKind.Modified, At(1), Attribution.None()));
        Assert.Equal(1, Of(b, 0, "e.txt").External);
    }

    [Fact]
    public void ExternalChangesOfTheTraceCountAsExternal()
    {
        FlowGraph flow = Flow([new UserMessage(H(0), "p"), new FileChanged(H(1), F("e.txt"), ChangeKind.Modified, null)]);
        Assert.Equal(new AreaFileActivity(F("e.txt"), 0, 0, 1, 0, int.MaxValue, int.MaxValue), Of(Build(flow), 0, "e.txt"));
    }

    [Fact]
    public void FilesOutsideTheProjectAndHiddenPathsAreIgnoredEverywhere()
    {
        var outside = new FilePath(OperatingSystem.IsWindows() ? @"C:\elsewhere\a.cs" : "/elsewhere/a.cs");
        FlowGraph flow = Flow(
        [
            new UserMessage(H(0), "p"), Call(1, "r"),
            new FileRead(H(1), outside, "r"), new FileRead(H(1), F(".claude/memory.md"), "r"), new FileRead(H(1), F("ok.txt"), "r"),
            new FileChanged(H(2), F(".git/index"), ChangeKind.Modified, null),
        ]);
        AreaActivity a = Build(flow,
            Git(".github/ci.yml", ChangeKind.Modified, At(1), Attribution.None()),
            new ChangeEntry(new FileChange(outside, ChangeKind.Modified, At(1)), Attribution.None()));
        Assert.Equal([F("ok.txt")], a.Turns.Single().Files.Select(f => f.Path).ToArray());
    }

    [Fact]
    public void AnErrorLandsOnTheTargetOfTheFailedCall()
    {
        FlowGraph flow = Flow(
        [
            new UserMessage(H(0), "p"), Call(1, "bad", F("src/a.cs")), new ToolResult(H(2), "bad", true, "boom"),
            Call(3, "fine", F("src/b.cs")), new ToolResult(H(4), "fine", false, "ok"),
            Call(5, "none"), new ToolResult(H(6), "none", true, "boom"),
            Call(7, "away", new FilePath(OperatingSystem.IsWindows() ? @"C:\x\y.cs" : "/x/y.cs")), new ToolResult(H(8), "away", true, "boom"),
        ]);
        AreaActivity a = Build(flow);
        Assert.Equal(new AreaFileActivity(F("src/a.cs"), 0, 0, 0, 1, 0), Of(a, 0, "src/a.cs"));
        Assert.Single(a.Turns.Single().Files);
    }

    [Fact]
    public void FirstTouchOrderFollowsTheTimeOfTheCallsAndPutsUnexplainedChangesLast()
    {
        FlowGraph flow = Flow(
        [
            new UserMessage(H(0), "p"),
            Call(1, "first"), new ToolResult(H(2), "first", false, "ok"),
            Call(3, "second"), new ToolResult(H(4), "second", false, "ok"),
            new FileRead(H(2), F("z.cs"), "first"), new FileChanged(H(4), F("a.cs"), ChangeKind.Modified, "second"),
            new FileChanged(H(5), F("m.cs"), ChangeKind.Modified, null),
            new FileRead(H(6), F("a.cs"), "first"),
        ]);
        AreaActivity a = Build(flow);
        Assert.Equal([F("a.cs"), F("z.cs"), F("m.cs")], a.Turns.Single().Files.Select(f => f.Path).ToArray());
        Assert.Equal(0, Of(a, 0, "z.cs").Order); // ties are ordered by path
        Assert.Equal(0, Of(a, 0, "a.cs").Order); // its read by "first" came first
        Assert.Equal(int.MaxValue, Of(a, 0, "m.cs").Order);
    }

    [Fact]
    public void SubagentCallsCountInTheTurnOfTheCallThatLaunchedThem()
    {
        FlowGraph flow = Flow(
        [
            new UserMessage(H(0), "p"), Call(1, "launch"),
            new SubagentStarted(H(1), "launch", "sub1", "d"),
            Call(2, "inner", sub: "sub1"), new FileChanged(H(3, "sub1"), F("deep.cs"), ChangeKind.Created, "inner"),
            new UserMessage(H(10), "next"),
        ]);
        AreaActivity a = Build(flow);
        Assert.Equal(1, Of(a, 0, "deep.cs").Writes);
    }

    [Fact]
    public void ManyChangesOfOneFileInATurnAddUp()
    {
        FlowGraph flow = Flow(
        [
            new UserMessage(H(0), "p"), Call(1, "a"), Call(2, "b"),
            new FileChanged(H(1), F("x.cs"), ChangeKind.Modified, "a"), new FileChanged(H(2), F("x.cs"), ChangeKind.Modified, "b"),
        ]);
        Assert.Equal(2, Of(Build(flow), 0, "x.cs").Writes);
    }
}
