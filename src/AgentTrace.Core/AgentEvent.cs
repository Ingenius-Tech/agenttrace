namespace AgentTrace.Core;

/// <summary>
/// Neutral event, independent of the agent that produced it.
/// Every line of a trace produces at least one event (a <see cref="Skipped"/> if it is not interpreted).
/// Hierarchy closed by convention (C# does not check exhaustiveness): every <c>switch</c> over an event ends in
/// <c>_ =&gt; throw new UnreachableException()</c> and a test covers each variant.
/// </summary>
public abstract record AgentEvent(EventHeader Header)
{
    /// <summary>Kind of a tool, decided by the adapter: the core does not know any agent's tool names.</summary>
    public enum ToolKind { Read, Write, Execute, Search, Delegate, Other }

    public enum ChangeKind { Created, Modified, Deleted }

    /// <summary>Why a line produces no event of its own (<c>Reason</c> in Java; renamed because <c>Skipped.Reason</c> would clash with the type).</summary>
    public enum SkipReason { Ignored, Unknown, Malformed }

    public sealed record UserMessage(EventHeader Header, string Text) : AgentEvent(Header);

    public sealed record AssistantText(EventHeader Header, string Text) : AgentEvent(Header);

    /// <summary>Reasoning step. The text may be empty and the duration null.</summary>
    public sealed record Thinking(EventHeader Header, string Text, TimeSpan? Duration) : AgentEvent(Header);

    /// <summary>
    /// Call to a tool.
    /// <c>Kind</c>: kind of the tool (<c>Other</c> if the adapter does not recognise it).
    /// <c>Summary</c>: short text the adapter chooses to display (path, command, pattern, description); null is stored as empty.
    /// <c>InputJson</c>: input as is, as text, so the core is not coupled to a JSON parser.
    /// <c>Target</c>: main file the tool acts on, if the adapter knows it (null if unknown or not applicable);
    /// it allows the call's errors to be attributed to a file.
    /// </summary>
    public sealed record ToolCall(EventHeader Header, string CallId, string ToolName, ToolKind Kind, string Summary, string InputJson)
        : AgentEvent(Header)
    {
        public string Summary { get; init; } = Summary ?? "";

        public FilePath? Target { get; init; }

        /// <summary>Shortcut for tests and unclassified tools: kind <c>Other</c> and no summary.</summary>
        public ToolCall(EventHeader header, string callId, string toolName, string inputJson)
            : this(header, callId, toolName, ToolKind.Other, "", inputJson)
        {
        }
    }

    /// <summary>
    /// The agent has delegated work to a subagent: the call <c>ParentCallId</c> launched the thread <c>SubagentId</c>
    /// (the same one carried in their header by the events of that thread). May be emitted more than once for the same subagent.
    /// </summary>
    public sealed record SubagentStarted(EventHeader Header, string ParentCallId, string SubagentId, string Description) : AgentEvent(Header);

    public sealed record ToolResult(EventHeader Header, string CallId, bool Error, string Output) : AgentEvent(Header);

    /// <summary>File changed. <c>CallId</c> is null if the change could not be attributed to a tool (external change).</summary>
    public sealed record FileChanged(EventHeader Header, FilePath Path, ChangeKind Kind, string? CallId) : AgentEvent(Header);

    /// <summary>
    /// The agent has read a file. Pure observability (what it consulted before changing something); it does not count as a change.
    /// Only reads done with a read tool are seen: those done through a console command are not detected.
    /// </summary>
    public sealed record FileRead(EventHeader Header, FilePath Path, string? CallId) : AgentEvent(Header);

    public sealed record TokenUsage(EventHeader Header, string Model, long Input, long Output, long CacheRead, long CacheCreation)
        : AgentEvent(Header);

    public sealed record TurnEnded(EventHeader Header, TimeSpan? Duration) : AgentEvent(Header);

    /// <summary>Line that produces no event of its own: deliberately ignored, of unknown type or corrupt.</summary>
    public sealed record Skipped(EventHeader Header, string RawType, SkipReason Reason) : AgentEvent(Header);
}
