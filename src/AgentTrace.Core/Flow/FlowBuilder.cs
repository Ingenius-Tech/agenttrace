using System.Diagnostics;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Core.Flow;

/// <summary>
/// Converts the event stream of a session into a <see cref="FlowGraph"/>, incrementally: it is fed event by event
/// and <see cref="Snapshot"/> gives the history up to that moment. The result depends only on the order of the events of
/// each lane, not on how they are grouped into batches. A single thread (the feeding one); the returned <c>FlowGraph</c> is immutable.
/// <para>
/// Rules: each call is a step; consecutive calls of a lane, with no result, text or reasoning between them, form a
/// batch. What the agent thinks or says is noted on the next batch; the last text of a turn is its answer. A user
/// prompt opens a turn; if the agent keeps working after it closed, a continuation turn without a prompt is opened.
/// </para>
/// </summary>
public sealed class FlowBuilder
{
    private readonly LaneState _main = new(MainLaneId);
    private readonly OrderedMap<string, LaneState> _subagents = new();
    private readonly List<TurnState> _turns = [];
    private readonly List<BatchState> _batches = [];
    private readonly Dictionary<string, MutableStep> _steps = [];

    // What arrives before the call it refers to (lanes are read separately).
    private readonly Dictionary<string, ToolResult> _earlyResults = [];
    private readonly Dictionary<string, List<FileTouch>> _earlyTouches = [];
    private readonly Dictionary<string, string> _earlySubagents = [];
    private readonly HashSet<FilePath> _leadExternal = [];

    public void Accept(AgentEvent evt)
    {
        string? subagentId = evt.Header.SubagentId;
        LaneState lane = subagentId == null ? _main : SubagentLane(subagentId);
        DateTimeOffset? ts = evt.Header.Timestamp;
        if (lane == _main && evt is not Skipped) TouchTurn(ts);
        switch (evt)
        {
            case UserMessage e:
                OnUserMessage(lane, e.Text, ts);
                break;
            case AssistantText e:
                lane.OpenBatch = null;
                if (lane == _main) EnsureTurn(ts);
                if (!string.IsNullOrWhiteSpace(e.Text)) lane.Pending.Texts.Add(e.Text);
                break;
            case Thinking e:
                lane.OpenBatch = null;
                if (lane == _main) EnsureTurn(ts);
                lane.Pending.ThinkingCount++;
                if (e.Duration is { } duration) lane.Pending.ThinkingTime += duration;
                if (!string.IsNullOrWhiteSpace(e.Text))
                {
                    if (lane.Pending.ThinkingText.Length > 0) lane.Pending.ThinkingText += "\n\n";
                    lane.Pending.ThinkingText += e.Text;
                }
                break;
            case ToolCall e:
                OnToolCall(lane, e, ts);
                break;
            case ToolResult e:
                OnToolResult(lane, e);
                break;
            case FileChanged e:
                OnFileChanged(e);
                break;
            case FileRead e:
                if (e.CallId != null) Touch(e.CallId, new FileTouch(e.Path, null));
                break;
            case SubagentStarted e:
                OnSubagentStarted(e);
                break;
            case TokenUsage e:
                var usage = new Tokens(e.Input, e.Output, e.CacheRead, e.CacheCreation);
                if (lane.OpenBatch != null) lane.OpenBatch.Tokens = lane.OpenBatch.Tokens.Plus(usage);
                else lane.Pending.Tokens = lane.Pending.Tokens.Plus(usage);
                break;
            case TurnEnded e:
                lane.OpenBatch = null;
                if (lane == _main) OnTurnEnded(e, ts);
                break;
            case Skipped:
                break;
            default:
                throw new UnreachableException();
        }
    }

    public FlowGraph Snapshot()
    {
        var lanes = new List<Lane> { new(MainLaneId, null, null, null, null, -1) };
        foreach (LaneState s in _subagents.Values)
        {
            lanes.Add(new Lane(s.Id, s.Id, s.ParentCallId, s.Title ?? s.Id, s.Task, s.TurnIndex));
        }
        var turnList = new List<Turn>();
        for (int i = 0; i < _turns.Count; i++) turnList.Add(TurnOf(_turns[i], i == _turns.Count - 1));
        // Per lane, in the order of each one: so the result does not depend on how the lanes were interleaved when read.
        // OrderBy is stable (like Java's List.sort): within a lane the creation order is kept.
        List<string> laneOrder = lanes.Select(l => l.Id).ToList();
        List<Batch> batchList = _batches.Select(b => b.Freeze()).OrderBy(b => laneOrder.IndexOf(b.LaneId)).ToList();
        return new FlowGraph(lanes.ToValueList(), turnList.ToValueList(), batchList.ToValueList(), _earlyResults.Count);
    }

    // --- events ---

    private void OnUserMessage(LaneState lane, string text, DateTimeOffset? ts)
    {
        lane.OpenBatch = null;
        if (lane != _main)
        {
            lane.Task ??= text;
            return;
        }
        FinishTurn(CurrentTurn());
        var turn = new TurnState(_turns.Count, text, ts);
        foreach (FilePath path in _leadExternal) turn.ExternalPaths.Add(path);
        _leadExternal.Clear();
        _turns.Add(turn);
    }

    private void OnToolCall(LaneState lane, ToolCall call, DateTimeOffset? ts)
    {
        if (_steps.ContainsKey(call.CallId)) return;
        if (lane == _main) EnsureTurn(ts);
        int turn = lane == _main ? Math.Max(_turns.Count - 1, 0) : lane.TurnIndex;
        if (lane.OpenBatch == null)
        {
            var batch = new BatchState(lane, turn, lane.BatchCount++, ts, lane.Pending.Freeze(), lane.Pending.Tokens);
            lane.Pending = new Pending();
            lane.OpenBatch = batch;
            _batches.Add(batch);
        }
        var step = new MutableStep(call, ts, turn);
        lane.OpenBatch.Steps.Add(step);
        _steps[call.CallId] = step;
        if (lane == _main) CurrentTurn()!.Steps.Add(step);
        if (_earlyResults.Remove(call.CallId, out ToolResult? early)) ApplyResult(step, early);
        if (_earlyTouches.Remove(call.CallId, out List<FileTouch>? touches))
        {
            foreach (FileTouch t in touches) AddTouch(step, t);
        }
        if (_earlySubagents.Remove(call.CallId, out string? subagent)) LinkSubagent(step, subagent);
    }

    private void OnToolResult(LaneState lane, ToolResult result)
    {
        lane.OpenBatch = null;
        if (_steps.TryGetValue(result.CallId, out MutableStep? step)) ApplyResult(step, result);
        else _earlyResults[result.CallId] = result;
    }

    private void OnFileChanged(FileChanged change)
    {
        if (change.CallId != null)
        {
            Touch(change.CallId, new FileTouch(change.Path, change.Kind));
        }
        else if (_turns.Count == 0)
        {
            _leadExternal.Add(change.Path);
        }
        else
        {
            CurrentTurn()!.ExternalPaths.Add(change.Path);
        }
    }

    private void OnSubagentStarted(SubagentStarted e)
    {
        LaneState lane = SubagentLane(e.SubagentId);
        lane.ParentCallId = e.ParentCallId;
        if (!string.IsNullOrWhiteSpace(e.Description)) lane.Title = e.Description;
        if (_steps.TryGetValue(e.ParentCallId, out MutableStep? step)) LinkSubagent(step, e.SubagentId);
        else _earlySubagents[e.ParentCallId] = e.SubagentId;
    }

    private void OnTurnEnded(TurnEnded e, DateTimeOffset? ts)
    {
        TurnState? turn = CurrentTurn();
        if (turn == null || turn.Closed) return;
        FinishTurn(turn);
        turn.Closed = true;
        turn.AgentDuration = e.Duration;
        if (ts != null) turn.End = ts;
        foreach (MutableStep s in turn.Steps)
        {
            if (s.State == StepState.Running) s.State = StepState.NoResult;
        }
    }

    // --- pieces ---

    private LaneState SubagentLane(string id) => _subagents.GetOrAdd(id, k => new LaneState(k) { TurnIndex = Math.Max(_turns.Count - 1, 0) });

    private void LinkSubagent(MutableStep step, string subagentId)
    {
        step.SubagentId = subagentId;
        _subagents[subagentId].TurnIndex = step.Turn;
    }

    private void Touch(string callId, FileTouch touch)
    {
        if (_steps.TryGetValue(callId, out MutableStep? step))
        {
            AddTouch(step, touch);
            return;
        }
        if (!_earlyTouches.TryGetValue(callId, out List<FileTouch>? list)) _earlyTouches[callId] = list = [];
        list.Add(touch);
    }

    private static void AddTouch(MutableStep step, FileTouch touch)
    {
        if (!step.Files.Contains(touch)) step.Files.Add(touch);
    }

    private static void ApplyResult(MutableStep step, ToolResult result)
    {
        step.State = result.Error ? StepState.Error : StepState.Ok;
        step.Output = result.Output;
        step.End = result.Header.Timestamp;
    }

    private TurnState? CurrentTurn() => _turns.Count == 0 ? null : _turns[^1];

    /// <summary>Opens a continuation turn if none is open: the agent keeps working without the user asking anything.</summary>
    private void EnsureTurn(DateTimeOffset? ts)
    {
        TurnState? turn = CurrentTurn();
        if (turn != null && !turn.Closed) return;
        turn = new TurnState(_turns.Count, null, ts);
        foreach (FilePath path in _leadExternal) turn.ExternalPaths.Add(path);
        _leadExternal.Clear();
        _turns.Add(turn);
    }

    private void TouchTurn(DateTimeOffset? ts)
    {
        TurnState? turn = CurrentTurn();
        if (turn != null && !turn.Closed && ts is { } t && (turn.End == null || t > turn.End)) turn.End = ts;
    }

    /// <summary>The last thing the agent said in the turn is its answer; what it thought without acting is discarded.</summary>
    private void FinishTurn(TurnState? turn)
    {
        _main.OpenBatch = null;
        if (turn != null && turn.Answer == null && _main.Pending.Texts.Count > 0)
        {
            turn.Answer = _main.Pending.Texts[^1];
            turn.AnswerTokens = _main.Pending.Tokens;
        }
        _main.Pending = new Pending();
    }

    private Turn TurnOf(TurnState t, bool last)
    {
        string? answer = t.Answer;
        Tokens? answerTokens = t.AnswerTokens;
        if (answer == null && last && !t.Closed && _main.Pending.Texts.Count > 0)
        {
            answer = _main.Pending.Texts[^1];
            answerTokens = _main.Pending.Tokens;
        }
        TimeSpan? duration = t.AgentDuration;
        if (duration == null && t.Closed && t.Start is { } start && t.End is { } end) duration = end - start;
        if (answerTokens != null && answerTokens.Equals(Tokens.Zero)) answerTokens = null;
        return new Turn(t.Index, t.Prompt, t.Start, t.End, duration, t.Closed, answer, answerTokens, t.ExternalPaths.Count);
    }

    // --- mutable internal state ---

    private sealed class Pending
    {
        public int ThinkingCount;
        public TimeSpan ThinkingTime = TimeSpan.Zero;
        public string ThinkingText = "";
        public readonly List<string> Texts = [];
        public Tokens Tokens = Tokens.Zero;

        public Notes Freeze()
        {
            if (ThinkingCount == 0 && Texts.Count == 0) return Notes.None;
            return new Notes(ThinkingCount, ThinkingTime, ThinkingText, Texts.ToValueList());
        }
    }

    private sealed class LaneState(string id)
    {
        public readonly string Id = id;
        public string? ParentCallId;
        public string? Title;
        public string? Task;
        public int TurnIndex;
        public int BatchCount;
        public BatchState? OpenBatch;
        public Pending Pending = new();
    }

    private sealed class TurnState(int index, string? prompt, DateTimeOffset? start)
    {
        public readonly int Index = index;
        public readonly string? Prompt = prompt;
        public readonly DateTimeOffset? Start = start;
        public readonly List<MutableStep> Steps = [];
        public readonly HashSet<FilePath> ExternalPaths = [];
        public DateTimeOffset? End = start;
        public TimeSpan? AgentDuration;
        public bool Closed;
        public string? Answer;
        public Tokens? AnswerTokens;
    }

    private sealed class BatchState(LaneState lane, int turn, int seq, DateTimeOffset? start, Notes notes, Tokens tokens)
    {
        public readonly List<MutableStep> Steps = [];
        public Tokens Tokens = tokens;

        public Batch Freeze()
        {
            // A subagent belongs to the turn of the call that launched it, which is known after its first events.
            int turnIndex = lane.Id == MainLaneId ? turn : lane.TurnIndex;
            Tokens? t = Tokens.Equals(Tokens.Zero) ? null : Tokens;
            return new Batch(lane.Id, turnIndex, seq, start, Steps.Select(s => s.Freeze()).ToValueList(), notes, t);
        }
    }

    private sealed class MutableStep(ToolCall call, DateTimeOffset? start, int turn)
    {
        public readonly ToolCall Call = call;
        public readonly DateTimeOffset? Start = start;
        public readonly int Turn = turn;
        public readonly List<FileTouch> Files = [];
        public StepState State = StepState.Running;
        public string? Output;
        public DateTimeOffset? End;
        public string? SubagentId;

        public Step Freeze() => new(Call.CallId, Call.ToolName, Call.Kind, Call.Summary, Call.InputJson, State, Output,
            Start, End, Files.ToValueList(), SubagentId);
    }
}
