using System.Globalization;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Core.Tests.Flow;

public sealed class FlowBuilderTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);

    private static EventHeader H(string? subagent, int second) =>
        new(new SessionId("s"), "test", subagent, T0.AddSeconds(second), new SourceRef(new FilePath("t"), 1));

    private static ToolCall Call(string? sub, int s, string id, ToolKind kind) => new(H(sub, s), id, kind.ToString().ToUpperInvariant(), kind, id, "{}");

    private static FlowGraph Build(IEnumerable<AgentEvent> events)
    {
        var b = new FlowBuilder();
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }

    private static Step StepOf(FlowGraph g, string id) => g.FindStep(id)!;

    [Fact]
    public void EmptyGraphHasOnlyTheMainLane()
    {
        FlowGraph g = new FlowBuilder().Snapshot();
        Assert.Single(g.Lanes);
        Assert.True(g.MainLane.IsMain);
        Assert.Empty(g.Turns);
        Assert.Equal(0, g.StepCount);
    }

    [Fact]
    public void ConsecutiveCallsFormOneBatchAndAResultBreaksIt()
    {
        FlowGraph g = Build(
        [
            new UserMessage(H(null, 0), "do something"),
            Call(null, 1, "a", ToolKind.Read), Call(null, 1, "b", ToolKind.Search),
            new ToolResult(H(null, 2), "a", false, "ok"), new ToolResult(H(null, 3), "b", false, "ok"),
            Call(null, 4, "c", ToolKind.Execute),
        ]);
        Assert.Equal(2, g.Batches.Count);
        Assert.Equal(["a", "b"], g.Batches[0].Steps.Select(s => s.CallId).ToArray());
        Assert.Equal(["c"], g.Batches[1].Steps.Select(s => s.CallId).ToArray());
        Assert.Equal(0, g.Batches[0].Seq);
        Assert.Equal(1, g.Batches[1].Seq);
        Assert.Equal(StepState.Ok, StepOf(g, "a").State);
        Assert.Equal(StepState.Running, StepOf(g, "c").State);
        Assert.Equal(TimeSpan.FromSeconds(1), StepOf(g, "a").Duration);
    }

    [Fact]
    public void ThinkingAndTextBecomeNotesOfTheNextBatchAndTheLastTextIsTheAnswer()
    {
        FlowGraph g = Build(
        [
            new UserMessage(H(null, 0), "p"),
            new Thinking(H(null, 1), "thinking", TimeSpan.FromSeconds(2)), new Thinking(H(null, 1), "", null),
            new AssistantText(H(null, 2), "going to read"),
            new TokenUsage(H(null, 2), "m", 1, 2, 3, 4),
            Call(null, 3, "a", ToolKind.Read),
            new TokenUsage(H(null, 3), "m", 10, 20, 30, 40),
            new ToolResult(H(null, 4), "a", false, "ok"),
            new AssistantText(H(null, 5), "done"),
            new TokenUsage(H(null, 5), "m", 5, 6, 7, 8),
            new TurnEnded(H(null, 6), TimeSpan.FromSeconds(6)),
        ]);
        Batch b = g.Batches[0];
        Assert.Equal(2, b.Notes.ThinkingCount);
        Assert.Equal(TimeSpan.FromSeconds(2), b.Notes.ThinkingTime);
        Assert.Equal("thinking", b.Notes.ThinkingText);
        Assert.Equal(["going to read"], b.Notes.Texts);
        Assert.Equal(new Tokens(11, 22, 33, 44), b.Tokens);
        Turn t = g.Turns[0];
        Assert.Equal("done", t.Answer);
        Assert.Equal(new Tokens(5, 6, 7, 8), t.AnswerTokens);
        Assert.True(t.Closed);
        Assert.Equal(TimeSpan.FromSeconds(6), t.Duration);
    }

    [Fact]
    public void AUserMessageOpensATurnAndWorkAfterTheEndOpensAContinuation()
    {
        FlowGraph g = Build(
        [
            new UserMessage(H(null, 0), "one"), Call(null, 1, "a", ToolKind.Read),
            new ToolResult(H(null, 2), "a", false, "ok"), new TurnEnded(H(null, 3), null),
            Call(null, 4, "b", ToolKind.Read),
            new UserMessage(H(null, 5), "two"), Call(null, 6, "c", ToolKind.Read),
        ]);
        Assert.Equal(3, g.Turns.Count);
        Assert.Equal("one", g.Turns[0].Prompt);
        Assert.True(g.Turns[0].Closed);
        Assert.Null(g.Turns[1].Prompt); // continuation
        Assert.Equal("two", g.Turns[2].Prompt);
        Assert.False(g.Turns[2].Closed);
        Assert.Equal([0, 1, 2], g.Batches.Select(b => b.TurnIndex).ToArray());
    }

    [Fact]
    public void EndingATurnMarksPendingStepsAsWithoutResult()
    {
        FlowGraph g = Build(
        [
            new UserMessage(H(null, 0), "p"), Call(null, 1, "a", ToolKind.Execute), new TurnEnded(H(null, 2), null),
        ]);
        Assert.Equal(StepState.NoResult, StepOf(g, "a").State);
    }

    [Fact]
    public void AnUnclosedTurnKeepsItsStepsRunningAndShowsTheLastTextProvisionally()
    {
        FlowGraph g = Build(
        [
            new UserMessage(H(null, 0), "p"), Call(null, 1, "a", ToolKind.Execute),
            new ToolResult(H(null, 2), "a", false, "x"), new AssistantText(H(null, 3), "almost"),
        ]);
        Assert.False(g.Turns[0].Closed);
        Assert.Equal("almost", g.Turns[0].Answer);
    }

    [Fact]
    public void ResultsBeforeTheirCallAreAppliedLaterAndUnknownOnesAreOrphans()
    {
        var b = new FlowBuilder();
        b.Accept(new UserMessage(H(null, 0), "p"));
        b.Accept(new ToolResult(H(null, 1), "a", true, "failed"));
        b.Accept(new FileChanged(H(null, 1), new FilePath("x.txt"), ChangeKind.Created, "a"));
        Assert.Equal(1, b.Snapshot().OrphanResults);
        b.Accept(Call(null, 0, "a", ToolKind.Write));
        FlowGraph g = b.Snapshot();
        Assert.Equal(0, g.OrphanResults);
        Assert.Equal(StepState.Error, StepOf(g, "a").State);
        Assert.Equal("failed", StepOf(g, "a").Output);
        Assert.Equal([new FileTouch(new FilePath("x.txt"), ChangeKind.Created)], StepOf(g, "a").Files);
        Assert.Equal(1, g.ErrorCount);
    }

    [Fact]
    public void AttributesFilesToTheirCallAndCountsExternalChangesPerTurn()
    {
        var a = new FilePath("a.txt");
        FlowGraph g = Build(
        [
            new FileChanged(H(null, 0), new FilePath("before.txt"), ChangeKind.Modified, null),
            new UserMessage(H(null, 1), "p"), Call(null, 2, "w", ToolKind.Write),
            new FileChanged(H(null, 3), a, ChangeKind.Created, "w"), new FileChanged(H(null, 4), a, ChangeKind.Created, "w"),
            new FileRead(H(null, 4), new FilePath("r.txt"), "w"),
            new FileChanged(H(null, 5), new FilePath("e.txt"), ChangeKind.Modified, null),
            new FileChanged(H(null, 6), new FilePath("e.txt"), ChangeKind.Modified, null),
        ]);
        Assert.Equal([new FileTouch(a, ChangeKind.Created), new FileTouch(new FilePath("r.txt"), null)], StepOf(g, "w").Files);
        Assert.Equal(2, g.Turns[0].ExternalChanges); // e.txt (once) and before.txt, which precedes the first turn
    }

    [Fact]
    public void LinksSubagentsToTheCallThatLaunchedThemAndKeepsTheirOwnLane()
    {
        FlowGraph g = Build(SubagentSession());
        Assert.Equal(2, g.Lanes.Count);
        Lane lane = g.FindLane("sub1")!;
        Assert.Equal("c4", lane.ParentCallId);
        Assert.Equal("research", lane.Title);
        Assert.Equal("task", lane.Task);
        Assert.Equal(0, lane.TurnIndex);
        Assert.Equal("sub1", StepOf(g, "c4").SubagentId);
        Assert.Equal(["s1"], g.BatchesOf("sub1").SelectMany(b => b.Steps).Select(s => s.CallId).ToArray());
        Assert.Equal(0, g.BatchesOf("sub1")[0].TurnIndex);
    }

    [Fact]
    public void RepeatedLinksAndCallsAreIdempotent()
    {
        var events = new List<AgentEvent>(SubagentSession())
        {
            new SubagentStarted(H(null, 9), "c4", "sub1", "research"),
            Call(null, 1, "c1", ToolKind.Read),
        };
        FlowGraph g = Build(events);
        Assert.Equal(2, g.Lanes.Count);
        Assert.Equal(Build(SubagentSession()).StepCount, g.StepCount);
    }

    /// <summary>Session with two turns, a subagent, errors and changes; it is the base for the incrementality tests.</summary>
    private static List<AgentEvent> MainEvents() =>
    [
        new UserMessage(H(null, 0), "one"),
        new Thinking(H(null, 1), "x", TimeSpan.FromSeconds(1)), new AssistantText(H(null, 1), "looking"),
        Call(null, 2, "c1", ToolKind.Read), Call(null, 2, "c2", ToolKind.Search),
        new ToolResult(H(null, 3), "c1", false, "ok"), new ToolResult(H(null, 3), "c2", true, "bad"),
        new FileRead(H(null, 3), new FilePath("a.txt"), "c1"),
        Call(null, 4, "c3", ToolKind.Write),
        new ToolResult(H(null, 5), "c3", false, "ok"), new FileChanged(H(null, 5), new FilePath("a.txt"), ChangeKind.Modified, "c3"),
        Call(null, 6, "c4", ToolKind.Delegate), new ToolResult(H(null, 7), "c4", false, "launched"),
        new SubagentStarted(H(null, 7), "c4", "sub1", "research"),
        new FileChanged(H(null, 8), new FilePath("ext.txt"), ChangeKind.Created, null),
        new TokenUsage(H(null, 9), "m", 1, 1, 1, 1), new AssistantText(H(null, 9), "ready"),
        new TurnEnded(H(null, 10), TimeSpan.FromSeconds(10)),
        new UserMessage(H(null, 11), "two"), Call(null, 12, "c5", ToolKind.Execute), new TurnEnded(H(null, 13), null),
    ];

    private static List<AgentEvent> SubEvents() =>
    [
        new UserMessage(H("sub1", 7), "task"),
        Call("sub1", 8, "s1", ToolKind.Execute), new ToolResult(H("sub1", 9), "s1", false, "ok"),
        new AssistantText(H("sub1", 9), "end"),
    ];

    private static List<AgentEvent> SubagentSession()
    {
        var all = new List<AgentEvent>(MainEvents());
        all.AddRange(SubEvents());
        return all;
    }

    [Fact]
    public void TheSnapshotDoesNotDependOnHowTheEventsAreBatched()
    {
        List<AgentEvent> events = SubagentSession();
        FlowGraph whole = Build(events);

        var snapshotting = new FlowBuilder();
        foreach (AgentEvent e in events)
        {
            snapshotting.Accept(e);
            snapshotting.Snapshot();
        }
        Assert.Equal(whole, snapshotting.Snapshot()); // taking intermediate snapshots does not change the result

        Assert.Equal(6, whole.StepCount);
        Assert.Equal(2, whole.Turns.Count);
        Assert.Equal(StepState.NoResult, StepOf(whole, "c5").State);
        Assert.Equal("ready", whole.Turns[0].Answer);
    }

    [Fact]
    public void AnyInterleavingOfLanesThatKeepsEachLanesOrderGivesTheSameGraph()
    {
        FlowGraph expected = Build(SubagentSession());
        List<AgentEvent> main = MainEvents();
        List<AgentEvent> sub = SubEvents();
        // Divergence: .NET's and Java's Random(42) generate different sequences; the property checked is the same.
        var random = new Random(42);
        for (int round = 0; round < 200; round++)
        {
            var merged = new List<AgentEvent>();
            int i = 0;
            int j = 0;
            while (i < main.Count || j < sub.Count)
            {
                bool takeMain = j >= sub.Count || (i < main.Count && random.Next(2) == 0);
                merged.Add(takeMain ? main[i++] : sub[j++]);
            }
            var b = new FlowBuilder();
            foreach (AgentEvent e in merged)
            {
                b.Accept(e);
                if (random.Next(4) == 0) b.Snapshot();
            }
            Assert.True(expected == b.Snapshot(), "round " + round);
        }
    }

    // New compared to Java

    [Fact]
    public void AcceptsEveryEventVariantWithoutFailing()
    {
        var b = new FlowBuilder();

        foreach (AgentEvent e in EventSamples.OfEveryVariant()) b.Accept(e);

        FlowGraph g = b.Snapshot();
        Assert.Equal(1, g.StepCount);
        Assert.Equal(StepState.Ok, g.Batches[0].Steps[0].State);
    }

    [Fact]
    public void FlowGraphsWithTheSameContentAreEqualAndHaveTheSameHash()
    {
        FlowGraph a = Build(SubagentSession());
        FlowGraph b = Build(SubagentSession());

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Build(MainEvents()));
    }

    [Fact]
    public void BatchesAreOrderedByLaneThenByCreation()
    {
        FlowGraph g = Build(SubagentSession());

        Assert.Equal(["main", "main", "main", "main", "sub1"], g.Batches.Select(b => b.LaneId).ToArray());
        Assert.Equal([0, 1, 2, 3, 0], g.Batches.Select(b => b.Seq).ToArray());
    }
}
