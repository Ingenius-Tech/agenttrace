using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core;

/// <summary>
/// What is known about a file in this observation. <c>Kind</c>: how it changed relative to the baseline, or null
/// if the agent only read it. <c>Actor</c>: who changed (or read) it. <c>Read</c>: whether the agent read it in the session.
/// </summary>
public sealed record FileStatus(ChangeKind? Kind, Actor Actor, bool Read)
{
    public bool Changed => Kind is not null;
}
