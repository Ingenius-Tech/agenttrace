using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Ingest.Tests;

public sealed class ChangeFollowerTest
{
    /// <summary>Fake source: the current list is changed from the test and each query takes as long as indicated.</summary>
    private sealed class FakeSource : IChangeSource
    {
        public volatile IReadOnlyList<FileChange> Current = [];
        public volatile int QueryMillis;
        public volatile bool Fail;
        private int _queries;

        public int Queries => Volatile.Read(ref _queries);

        public IReadOnlyList<FileChange> Changes()
        {
            Interlocked.Increment(ref _queries);
            if (QueryMillis > 0) Thread.Sleep(QueryMillis);
            if (Fail) throw new IOException("git failed");
            return Current;
        }

        public FileVersions Versions(FilePath path) => FileVersions.Unavailable(path, UnavailableReason.Binary);

        public void ResetBaseline()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Recorder : IChangeFollowerListener
    {
        private readonly object _lock = new();
        private readonly List<IReadOnlyList<FileChange>> _updates = [];
        private readonly List<Exception> _errors = [];

        public List<IReadOnlyList<FileChange>> Updates { get { lock (_lock) return [.. _updates]; } }

        public List<Exception> Errors { get { lock (_lock) return [.. _errors]; } }

        public void ChangesUpdated(IReadOnlyList<FileChange> changes)
        {
            lock (_lock) _updates.Add(changes);
        }

        public void Error(Exception error)
        {
            lock (_lock) _errors.Add(error);
        }
    }

    private static FileChange Change(string name) => new(new FilePath(name), ChangeKind.Modified, null);

    private static ChangeFollower Follower(FakeSource source, Recorder r) =>
        new(source, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(200), r);

    [Fact]
    public void StartingDoesAFirstQueryAndNotifiesTheList()
    {
        var source = new FakeSource { Current = [Change("a")] };
        var r = new Recorder();
        using ChangeFollower f = Follower(source, r);
        f.Start();
        Eventually.Until("first list", () => r.Updates.Count == 1);
        Assert.Equal(source.Current, r.Updates[0]);
    }

    [Fact]
    public void DoesNotNotifyWhenTheListDidNotChange()
    {
        var source = new FakeSource { Current = [Change("a")] };
        var r = new Recorder();
        using ChangeFollower f = Follower(source, r);
        f.Start();
        Eventually.Until("first list", () => r.Updates.Count == 1);
        int before = source.Queries;
        f.Trigger();
        Eventually.Until("second query", () => source.Queries > before);
        Thread.Sleep(150);
        Assert.Single(r.Updates); // same list: no notification
        source.Current = [Change("a"), Change("b")];
        f.Trigger();
        Eventually.Until("new list", () => r.Updates.Count == 2);
    }

    [Fact]
    public void ABurstOfTriggersIsGroupedIntoFewQueriesAndEndsWithTheFinalState()
    {
        var source = new FakeSource();
        var r = new Recorder();
        using ChangeFollower f = Follower(source, r);
        f.Start();
        Eventually.Until("startup", () => source.Queries >= 1);
        int baseQueries = source.Queries;
        for (int i = 0; i < 500; i++)
        {
            source.Current = [Change("f" + i)];
            f.Trigger();
            Thread.Sleep(1);
        }
        Eventually.Until("final state", () =>
        {
            List<IReadOnlyList<FileChange>> updates = r.Updates;
            return updates.Count > 0 && updates[^1].SequenceEqual([Change("f499")]);
        });
        int queries = source.Queries - baseQueries;
        // Thread.Sleep(1) lasts ~15 ms on Windows: the burst lasts longer than in Java and there are a few more queries
        Assert.True(queries <= 60, "500 triggers produced " + queries + " queries");
        Assert.True(queries >= 1);
    }

    [Fact]
    public void TriggersDuringAQueryCauseAnotherOneAfterwards()
    {
        var source = new FakeSource { QueryMillis = 200 };
        var r = new Recorder();
        using ChangeFollower f = Follower(source, r);
        f.Start();
        Eventually.Until("query in progress", () => source.Queries == 1);
        source.Current = [Change("during")];
        f.Trigger();
        Eventually.Until("list updated after the slow query",
            () => r.Updates.Any(l => l.SequenceEqual([Change("during")])));
        Assert.True(source.Queries >= 2);
    }

    [Fact]
    public void ErrorsAreReportedAndTheFollowerKeepsWorking()
    {
        var source = new FakeSource { Fail = true };
        var r = new Recorder();
        using ChangeFollower f = Follower(source, r);
        f.Start();
        Eventually.Until("error", () => r.Errors.Count > 0);
        source.Fail = false;
        source.Current = [Change("a")];
        f.Trigger();
        Eventually.Until("recovery", () => r.Updates.Count > 0);
    }

    [Fact]
    public void DisposeStopsTheThreadPromptlyEvenWhileWaiting()
    {
        var source = new FakeSource();
        ChangeFollower f = Follower(source, new Recorder());
        f.Start();
        Eventually.Until("startup", () => source.Queries >= 1);
        long start = Stopwatch.GetTimestamp();
        f.Dispose();
        Assert.True(Stopwatch.GetElapsedTime(start).TotalMilliseconds < 1000);
        int queries = source.Queries;
        f.Trigger();
        Thread.Sleep(150);
        Assert.Equal(queries, source.Queries); // closed: it must not query again
    }
}
