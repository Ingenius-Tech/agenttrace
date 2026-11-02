using System.Diagnostics;
using AgentTrace.Core;

namespace AgentTrace.Ingest;

/// <summary>Whoever listens to a <see cref="ChangeFollower"/>. Called from the follower's thread.</summary>
public interface IChangeFollowerListener
{
    void ChangesUpdated(IReadOnlyList<FileChange> changes);

    void Error(Exception error)
    {
    }
}

/// <summary>
/// Keeps a project's change list up to date. Each notice (<see cref="Trigger"/>) causes a query to the
/// source, but batched: it waits for quiet (<c>debounce</c>) without exceeding <c>maxWait</c> since the first notice,
/// so continuous writing keeps refreshing. Only one query runs at a time; if notices arrive during it, it repeats.
/// It notifies only when the list has changed. Notifications are delivered from the follower's thread.
/// </summary>
public sealed class ChangeFollower(
    IChangeSource source,
    TimeSpan debounce,
    TimeSpan maxWait,
    IChangeFollowerListener listener) : IDisposable
{
    private readonly object _lock = new();
    private bool _dirty;
    private long _firstTrigger;
    private long _lastTrigger;
    private volatile bool _running;
    private Thread? _thread;
    private IReadOnlyList<FileChange>? _last;

    /// <summary>Starts the thread and runs a first query.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_thread is not null) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "agenttrace-change-follower" };
            _thread.Start();
        }
        Trigger();
    }

    /// <summary>There is something to query: from the watcher, or by hand (for example, after resetting the baseline).</summary>
    public void Trigger()
    {
        lock (_lock)
        {
            long now = Stopwatch.GetTimestamp();
            if (!_dirty)
            {
                _dirty = true;
                _firstTrigger = now;
            }
            _lastTrigger = now;
            Monitor.PulseAll(_lock);
        }
    }

    private void Loop()
    {
        while (_running)
        {
            lock (_lock)
            {
                while (_running && !_dirty) Monitor.Wait(_lock);
                while (_running)
                {
                    long now = Stopwatch.GetTimestamp();
                    TimeSpan quiet = Stopwatch.GetElapsedTime(_lastTrigger, now);
                    TimeSpan waited = Stopwatch.GetElapsedTime(_firstTrigger, now);
                    if (quiet >= debounce || waited >= maxWait) break;
                    TimeSpan remaining = TimeSpan.FromTicks(Math.Min((debounce - quiet).Ticks, (maxWait - waited).Ticks));
                    Monitor.Wait(_lock, remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining);
                }
                if (!_running) return;
                _dirty = false;
            }
            Refresh();
        }
    }

    private void Refresh()
    {
        try
        {
            IReadOnlyList<FileChange> current = source.Changes();
            if (_last is null || !current.SequenceEqual(_last))
            {
                _last = current;
                listener.ChangesUpdated(current);
            }
        }
        catch (Exception e)
        {
            listener.Error(e);
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_lock)
        {
            _running = false;
            thread = _thread;
            _thread = null;
            Monitor.PulseAll(_lock);
        }
        thread?.Join(TimeSpan.FromSeconds(2));
    }
}
