using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests;

/// <summary>One instance of each variant of <see cref="AgentEvent"/>, for tests that must cover them all.</summary>
internal static class EventSamples
{
    public static readonly EventHeader Header = new(
        new SessionId("s"), "test", null, DateTimeOffset.Parse("2026-01-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        new SourceRef(new FilePath("t.jsonl"), 1));

    public static IReadOnlyList<AgentEvent> OfEveryVariant() =>
    [
        new UserMessage(Header, "hello"),
        new AssistantText(Header, "text"),
        new Thinking(Header, "thinking", TimeSpan.FromSeconds(1)),
        new ToolCall(Header, "c1", "Bash", ToolKind.Execute, "ls", "{}"),
        new SubagentStarted(Header, "c1", "sub", "investigate"),
        new ToolResult(Header, "c1", false, "ok"),
        new FileChanged(Header, new FilePath("a.txt"), ChangeKind.Created, "c1"),
        new FileRead(Header, new FilePath("a.txt"), "c1"),
        new TokenUsage(Header, "m", 1, 2, 3, 4),
        new TurnEnded(Header, TimeSpan.FromSeconds(2)),
        new Skipped(Header, "x", SkipReason.Unknown),
    ];
}
