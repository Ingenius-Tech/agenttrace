using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core;

/// <summary>
/// Relates the file changes on disk to the tool calls of the trace, using only the time
/// (that of the change is the file's last modification) and the path. It knows nothing about agents or tool names.
/// <para>
/// Rules, in order: 1) exact: the trace records that a call wrote that file and the time matches;
/// 2) probable or ambiguous: calls in progress when the file was modified, discarding those the trace declares
/// as writers of other files (they cannot have caused this one) and those that contain another, for example the call that
/// launches a subagent versus what the subagent does; 3) none.
/// Not thread-safe.
/// </para>
/// </summary>
public sealed class ChangeCorrelator
{
    /// <summary>Margin between the file's time and the call result's time.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private sealed class CallSpan(ToolCall call, DateTimeOffset start)
    {
        public readonly ToolCall Call = call;
        public readonly DateTimeOffset Start = start;
        public DateTimeOffset? End;

        public bool Contains(DateTimeOffset t) => t >= Start && (End is null || t <= End.Value + Grace);

        /// <summary>This call completely spans the other.</summary>
        public bool Encloses(CallSpan other)
        {
            if (this == other || other.Start < Start) return false;
            DateTimeOffset myEnd = End ?? DateTimeOffset.MaxValue;
            DateTimeOffset otherEnd = other.End ?? DateTimeOffset.MaxValue;
            bool sameInterval = Start == other.Start && myEnd == otherEnd;
            return !sameInterval && otherEnd <= myEnd;
        }
    }

    private sealed record Write(string CallId, DateTimeOffset Time);

    // Insertion order: Java uses HashMap, whose order is undefined; here time ties are resolved by arrival order.
    private readonly OrderedMap<string, CallSpan> _spans = new();
    private readonly Dictionary<FilePath, List<Write>> _writes = [];

    /// <summary>Files the trace declares as written by each call: such a call only touches those.</summary>
    private readonly Dictionary<string, HashSet<FilePath>> _declaredByCall = [];

    /// <summary>Feeds the correlator with the trace events, in the order they arrive.</summary>
    public void Accept(AgentEvent evt)
    {
        DateTimeOffset? time = evt.Header.Timestamp;
        switch (evt)
        {
            case ToolCall e:
                if (time is { } start) _spans[e.CallId] = new CallSpan(e, start);
                break;
            case ToolResult e:
                if (_spans.TryGetValue(e.CallId, out CallSpan? span) && time is { } end) span.End = end;
                break;
            case FileChanged e:
                if (e.CallId != null && time is { } at)
                {
                    if (!_writes.TryGetValue(e.Path, out List<Write>? list)) _writes[e.Path] = list = [];
                    list.Add(new Write(e.CallId, at));
                    if (!_declaredByCall.TryGetValue(e.CallId, out HashSet<FilePath>? declared)) _declaredByCall[e.CallId] = declared = [];
                    declared.Add(e.Path);
                }
                break;
            default:
                break;
        }
    }

    /// <summary>Forgets everything received (when the session changes).</summary>
    public void Reset()
    {
        _spans.Clear();
        _writes.Clear();
        _declaredByCall.Clear();
    }

    public Attribution Attribute(FileChange change)
    {
        if (change.ModifiedAt is not { } fileTime) return Attribution.Undated();

        Write? exact = LatestWriteNear(change.Path, fileTime);
        if (exact != null && _spans.TryGetValue(exact.CallId, out CallSpan? exactSpan))
        {
            return new Attribution(Confidence.Exact, [exactSpan.Call]);
        }

        FilePath path = change.Path;
        var running = new List<CallSpan>();
        foreach (CallSpan span in _spans.Values)
        {
            bool writesElsewhere = _declaredByCall.TryGetValue(span.Call.CallId, out HashSet<FilePath>? declared) && !declared.Contains(path);
            if (span.Contains(fileTime) && !writesElsewhere) running.Add(span);
        }
        List<CallSpan> innermost = running
            .Where(outer => !running.Any(inner => outer.Encloses(inner) && RunsInASubagentOf(inner, outer)))
            .OrderBy(s => s.Start) // stable: ties keep the arrival order
            .ToList();
        if (innermost.Count == 0) return Attribution.None();
        ValueList<ToolCall> calls = innermost.Select(s => s.Call).ToValueList();
        return new Attribution(calls.Count == 1 ? Confidence.Probable : Confidence.Ambiguous, calls);
    }

    /// <summary>
    /// The inner call runs in a subagent and the outer one does not (it is the one that launched it and awaits its result).
    /// Two simultaneous calls of the same thread are not resolved this way: they remain ambiguous.
    /// </summary>
    private static bool RunsInASubagentOf(CallSpan inner, CallSpan outer)
    {
        string? innerAgent = inner.Call.Header.SubagentId;
        return innerAgent != null && innerAgent != outer.Call.Header.SubagentId;
    }

    /// <summary>The last write recorded in the trace for that path whose time matches the file's.</summary>
    private Write? LatestWriteNear(FilePath path, DateTimeOffset fileTime)
    {
        Write? best = null;
        if (!_writes.TryGetValue(path, out List<Write>? candidates)) return null;
        foreach (Write w in candidates)
        {
            bool near = w.Time >= fileTime - Grace && w.Time <= fileTime + Grace;
            if (near && (best == null || w.Time > best.Time)) best = w;
        }
        return best;
    }
}
