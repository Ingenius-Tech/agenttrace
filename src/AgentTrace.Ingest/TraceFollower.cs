using System.Diagnostics;
using AgentTrace.Core;

namespace AgentTrace.Ingest;

/// <summary>Listener of a <see cref="TraceFollower"/>. Called from the follower's thread.</summary>
public interface ITraceFollowerListener
{
    /// <summary>Switched to another session; the events that follow belong to it.</summary>
    void SessionChanged(FilePath sessionTrace);

    void Events(IReadOnlyList<AgentEvent> events);

    void Error(Exception error)
    {
    }
}

/// <summary>
/// Follows live the active session of a project: the most recently modified session trace, together with
/// its associated traces (subagents). When another session starts, it switches to it and notifies.
/// Read-only. Notices are delivered from the follower's thread: a listener must hop to its own thread if needed.
/// </summary>
public sealed class TraceFollower(
    IAgentAdapter adapter,
    FilePath projectDir,
    TimeSpan pollInterval,
    TimeSpan discoveryInterval,
    ITraceFollowerListener listener) : IDisposable
{
    private sealed class Source(TraceFileTailer tailer, ILineParser parser)
    {
        public TraceFileTailer Tailer { get; } = tailer;

        public ILineParser Parser { get; } = parser;

        /// <summary>Last timestamp seen in this file: inherited by lines that carry no time.</summary>
        public DateTimeOffset LastTimestamp { get; set; } = DateTimeOffset.MinValue;
    }

    private readonly object _gate = new();
    private readonly Dictionary<FilePath, Source> _sources = [];
    private readonly List<FilePath> _order = [];
    private FilePath? _currentSession;
    private long _lastDiscovery;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null) return;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _loop = Task.Run(() => Run(token), token);
        }
    }

    private async Task Run(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick();
                await Task.Delay(pollInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                listener.Error(e);
            }
        }
    }

    /// <summary>One pass: discovers the active session and delivers the new events. Public so it can be tested without threads.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_currentSession is null || Stopwatch.GetElapsedTime(_lastDiscovery) >= discoveryInterval)
            {
                _lastDiscovery = Stopwatch.GetTimestamp();
                Discover();
            }
            List<(DateTimeOffset Time, AgentEvent Event)> stamped = [];
            foreach (FilePath path in _order)
            {
                Source source = _sources[path];
                try
                {
                    foreach (RawLine line in source.Tailer.Poll())
                    {
                        foreach (AgentEvent evt in source.Parser.Parse(line.Text, line.Number))
                        {
                            if (evt.Header.Timestamp is { } time) source.LastTimestamp = time;
                            stamped.Add((source.LastTimestamp, evt));
                        }
                    }
                }
                catch (IOException e)
                {
                    listener.Error(e);
                }
            }
            if (stamped.Count == 0) return;
            // Session and subagents are read separately: they are sorted by time (stable order, respects each file's)
            listener.Events([.. stamped.OrderBy(s => s.Time).Select(s => s.Event)]);
        }
    }

    private void Discover()
    {
        IReadOnlyList<FilePath> sessions = adapter.FindTraceFiles(projectDir);
        if (sessions.Count == 0) return;
        FilePath newest = sessions[0];
        if (_currentSession != newest)
        {
            _currentSession = newest;
            _sources.Clear();
            _order.Clear();
            AddSource(newest);
            listener.SessionChanged(newest);
        }
        foreach (FilePath related in adapter.RelatedTraceFiles(_currentSession.Value)) AddSource(related);
    }

    private void AddSource(FilePath file)
    {
        if (_sources.ContainsKey(file)) return;
        _sources[file] = new Source(new TraceFileTailer(file), adapter.NewParser(file));
        _order.Add(file);
    }

    public void Dispose()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }
        if (cts is null) return;
        cts.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The task ended by cancellation
        }
        cts.Dispose();
    }
}
