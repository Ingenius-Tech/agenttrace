namespace AgentTrace.Core;

/// <summary>
/// Data common to all events. <c>SubagentId</c> is null if the event belongs to the main thread;
/// <c>Timestamp</c> is null if the trace does not carry it (always UTC).
/// </summary>
public sealed record EventHeader(SessionId SessionId, string Agent, string? SubagentId, DateTimeOffset? Timestamp, SourceRef Source);
