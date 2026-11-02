using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Graph;

/// <summary>Headers, calls and graph construction that the graph tests repeat (in Java, each class defines them).</summary>
internal static class GraphTestKit
{
    public static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);

    public static EventHeader H(string? sub, int second) =>
        new(new SessionId("s"), "test", sub, T0.AddSeconds(second), new SourceRef(new FilePath("t"), 1));

    public static ToolCall Call(string? sub, int s, string id, ToolKind kind) => new(H(sub, s), id, id, kind, id, "{}");

    public static FlowGraph BuildFlow(IEnumerable<AgentEvent> events)
    {
        var b = new FlowBuilder();
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }
}
