using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core;

/// <summary>
/// How much confidence there is in an attribution (<c>Attribution.Confidence</c> in Java; it is top-level because
/// <c>Attribution.Confidence</c> is also the property).
/// </summary>
public enum Confidence
{
    /// <summary>The trace says that call wrote the file and the time matches.</summary>
    Exact,

    /// <summary>A single call was in progress when the file was modified.</summary>
    Probable,

    /// <summary>Several calls were in progress: all are shown.</summary>
    Ambiguous,

    /// <summary>No call in progress: external change (user or another process) or by an agent that is not followed.</summary>
    None,

    /// <summary>The time of the change is unknown (a deleted file has no modification date).</summary>
    Undated,
}

/// <summary>Which agent action a file change is attributed to. <c>Calls</c>: candidate calls (empty if there is no attribution).</summary>
public sealed record Attribution(Confidence Confidence, ValueList<ToolCall> Calls)
{
    public static Attribution None() => new(Confidence.None, ValueList<ToolCall>.Empty);

    public static Attribution Undated() => new(Confidence.Undated, ValueList<ToolCall>.Empty);
}
