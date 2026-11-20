using System.Globalization;
using AgentTrace.Core.Areas;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests.Areas;

public sealed class TaskGroupingTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);

    private static EventHeader H(string? subagent, int second) =>
        new(new SessionId("s"), "test", subagent, T0.AddSeconds(second), new SourceRef(new FilePath("t"), 1));

    private static ValueList<AreaTask> Group(params AgentEvent[] events)
    {
        var builder = new FlowBuilder();
        foreach (AgentEvent e in events) builder.Accept(e);
        return TaskGrouping.Of(builder.Snapshot().Turns);
    }

    private static ToolCall Call(string? sub, int s, string id) => new(H(sub, s), id, "T", ToolKind.Other, id, "{}");

    [Fact]
    public void NoTurnsMakeNoTasks()
    {
        Assert.Empty(TaskGrouping.Of([]));
    }

    [Fact]
    public void EachPromptOpensATask()
    {
        ValueList<AreaTask> tasks = Group(
            new UserMessage(H(null, 0), "one"), Call(null, 1, "a"), new TurnEnded(H(null, 2), null),
            new UserMessage(H(null, 3), "two"), new UserMessage(H(null, 4), "three"));
        Assert.Equal([new AreaTask(0, 0, 0, true), new AreaTask(1, 1, 1, true), new AreaTask(2, 2, 2, true)], tasks);
    }

    [Fact]
    public void AContinuationTurnJoinsThePreviousTask()
    {
        ValueList<AreaTask> tasks = Group(
            new UserMessage(H(null, 0), "one"), Call(null, 1, "a"), new TurnEnded(H(null, 2), null),
            Call(null, 3, "b"), new TurnEnded(H(null, 4), null), // continuation turn 1
            Call(null, 5, "c"), new TurnEnded(H(null, 6), null), // continuation turn 2
            new UserMessage(H(null, 7), "two"));
        Assert.Equal([new AreaTask(0, 0, 2, true), new AreaTask(1, 3, 3, true)], tasks);
    }

    [Fact]
    public void ALeadingContinuationOpensATaskWithoutPrompt()
    {
        ValueList<AreaTask> tasks = Group(
            Call(null, 0, "a"), new TurnEnded(H(null, 1), null),
            Call(null, 2, "b"), new TurnEnded(H(null, 3), null),
            new UserMessage(H(null, 4), "now"));
        Assert.Equal([new AreaTask(0, 0, 1, false), new AreaTask(1, 2, 2, true)], tasks);
    }

    [Fact]
    public void SubagentWorkOpensNoTurnsAndNoTasks()
    {
        ValueList<AreaTask> tasks = Group(
            new UserMessage(H(null, 0), "one"), Call(null, 1, "launch"),
            new SubagentStarted(H(null, 1), "launch", "sub1", "d"),
            new UserMessage(H("sub1", 2), "do it"), Call("sub1", 3, "x"), new ToolResult(H("sub1", 4), "x", false, "ok"),
            new TurnEnded(H("sub1", 5), null));
        Assert.Equal([new AreaTask(0, 0, 0, true)], tasks);
    }

    [Fact]
    public void ATaskWithNoActivityAtAllStillCoversItsTurn()
    {
        Assert.Equal([new AreaTask(0, 0, 0, true)], Group(new UserMessage(H(null, 0), "hello")));
    }
}
