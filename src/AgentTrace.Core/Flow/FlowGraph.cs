using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Flow;

/// <summary>
/// The history of a session: lanes (the main thread and one lane per subagent), turns (each user prompt opens one) and,
/// inside them, batches of steps (the calls the agent launched at once). It is immutable; it is obtained from a
/// <see cref="FlowBuilder"/>. It does not depend on any specific agent or on Avalonia.
/// <c>OrphanResults</c>: results whose call was never seen (for example, after the conversation was compacted).
/// </summary>
public sealed record FlowGraph(ValueList<FlowGraph.Lane> Lanes, ValueList<FlowGraph.Turn> Turns, ValueList<FlowGraph.Batch> Batches, int OrphanResults)
{
    /// <summary>Identifier of the main thread lane (<c>MAIN</c> in Java; <c>Main</c> is the Java <c>main()</c> method).</summary>
    public const string MainLaneId = "main";

    /// <summary>State of a step. <c>NoResult</c>: the turn ended before its result arrived.</summary>
    public enum StepState { Running, Ok, Error, NoResult }

    /// <summary>
    /// A thread of work.
    /// <c>Id</c>: <c>"main"</c> or the subagent identifier. <c>ParentCallId</c>: call that launched the subagent, or null.
    /// <c>Title</c>: description of what was delegated (null on the main lane: the UI names it). <c>Task</c>: the assignment the
    /// subagent received (its first message), or null. <c>TurnIndex</c>: turn a subagent belongs to (that of the
    /// call that launched it); -1 on the main lane.
    /// </summary>
    public sealed record Lane(string Id, string? SubagentId, string? ParentCallId, string? Title, string? Task, int TurnIndex)
    {
        public bool IsMain => MainLaneId == Id;
    }

    /// <summary>
    /// A user prompt and everything the agent did until finishing it.
    /// <c>Prompt</c>: the prompt, or null in a continuation turn (the agent kept going without the user asking anything).
    /// <c>Duration</c>: total duration according to the agent, or null if unknown. <c>Closed</c>: the agent indicated it
    /// finished; if false, the turn ended without a close or is still in progress. <c>Answer</c>: the text it answered
    /// with, or null. <c>AnswerTokens</c>: usage of the answer message, or null. <c>ExternalChanges</c>: files that
    /// changed during the turn without any call explaining them.
    /// </summary>
    public sealed record Turn(int Index, string? Prompt, DateTimeOffset? Start, DateTimeOffset? End, TimeSpan? Duration, bool Closed,
        string? Answer, Tokens? AnswerTokens, int ExternalChanges);

    /// <summary>
    /// Batches of calls launched at once in the same lane (the graph column).
    /// <c>Tokens</c>: usage of the message that generated the batch, shared by all its calls, or null.
    /// </summary>
    public sealed record Batch(string LaneId, int TurnIndex, int Seq, DateTimeOffset? Start, ValueList<Step> Steps, Notes Notes, Tokens? Tokens);

    /// <summary>What the agent thought or said right before launching a batch.</summary>
    public sealed record Notes(int ThinkingCount, TimeSpan ThinkingTime, string ThinkingText, ValueList<string> Texts)
    {
        public static readonly Notes None = new(0, TimeSpan.Zero, "", ValueList<string>.Empty);

        public bool IsEmpty => ThinkingCount == 0 && Texts.Count == 0;
    }

    /// <summary>A tool call with its result.</summary>
    public sealed record Step(string CallId, string ToolName, ToolKind Kind, string Summary, string InputJson, StepState State,
        string? Output, DateTimeOffset? Start, DateTimeOffset? End, ValueList<FileTouch> Files, string? SubagentId)
    {
        /// <summary>How long it took, if the call and the result are known.</summary>
        public TimeSpan? Duration => Start is null || End is null ? null : End.Value - Start.Value;
    }

    /// <summary>A file a step changed (<c>Kind</c> not null) or read (<c>Kind</c> null).</summary>
    public sealed record FileTouch(FilePath Path, ChangeKind? Kind);

    /// <summary>Token usage of an agent message.</summary>
    public sealed record Tokens(long Input, long Output, long CacheRead, long CacheCreation)
    {
        public static readonly Tokens Zero = new(0, 0, 0, 0);

        public Tokens Plus(Tokens other) =>
            new(Input + other.Input, Output + other.Output, CacheRead + other.CacheRead, CacheCreation + other.CacheCreation);
    }

    public Lane MainLane => Lanes[0];

    /// <summary>The lane with that identifier, or null (<c>lane(id)</c> in Java).</summary>
    public Lane? FindLane(string id) => Lanes.FirstOrDefault(l => l.Id == id);

    /// <summary>The step with that call, or null (<c>step(callId)</c> in Java).</summary>
    public Step? FindStep(string callId)
    {
        foreach (Batch batch in Batches)
        {
            foreach (Step step in batch.Steps)
            {
                if (step.CallId == callId) return step;
            }
        }
        return null;
    }

    public IReadOnlyList<Batch> BatchesOf(string laneId) => Batches.Where(b => b.LaneId == laneId).ToList();

    public int StepCount => Batches.Sum(b => b.Steps.Count);

    public int ErrorCount => Batches.SelectMany(b => b.Steps).Count(s => s.State == StepState.Error);
}
