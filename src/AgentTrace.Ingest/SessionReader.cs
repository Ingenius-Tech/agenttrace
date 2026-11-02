using AgentTrace.Core;

namespace AgentTrace.Ingest;

/// <summary>One-off, full reading of traces, without tailing. Uses the same code path as live tailing.</summary>
public static class SessionReader
{
    /// <summary>All the events of a file. The last line without a line break is also interpreted.</summary>
    public static IReadOnlyList<AgentEvent> ReadFile(IAgentAdapter adapter, FilePath traceFile)
    {
        var tailer = new TraceFileTailer(traceFile);
        ILineParser parser = adapter.NewParser(traceFile);
        List<AgentEvent> events = [];
        foreach (RawLine line in tailer.Poll()) events.AddRange(parser.Parse(line.Text, line.Number));
        RawLine? last = tailer.FlushPending();
        if (last is not null) events.AddRange(parser.Parse(last.Text, last.Number));
        return events;
    }

    /// <summary>Events of a session and its associated traces (subagents), the session first.</summary>
    public static IReadOnlyList<AgentEvent> ReadSession(IAgentAdapter adapter, FilePath sessionTrace)
    {
        List<AgentEvent> events = [.. ReadFile(adapter, sessionTrace)];
        foreach (FilePath related in adapter.RelatedTraceFiles(sessionTrace)) events.AddRange(ReadFile(adapter, related));
        return events;
    }
}
