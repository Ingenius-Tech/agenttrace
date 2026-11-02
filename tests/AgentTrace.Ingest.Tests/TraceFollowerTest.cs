using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Ingest.Tests;

/// <summary>Tests following with a fake adapter, to check that it does not depend on any agent.</summary>
public sealed class TraceFollowerTest : TempDirTestBase
{
    /// <summary>Each line is a user message; sessions are the .jsonl files of the folder; those in the "sub" subfolder are related.</summary>
    private sealed class FakeAdapter : IAgentAdapter
    {
        public string Agent => "fake";

        public bool Owns(FilePath f) => f.Value.EndsWith(".jsonl", StringComparison.Ordinal);

        public IReadOnlyList<FilePath> FindTraceFiles(FilePath dir)
        {
            try
            {
                return [.. Directory.EnumerateFiles(dir.Value, "*.jsonl")
                    .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
                    .Select(p => new FilePath(p))];
            }
            catch (IOException)
            {
                return [];
            }
        }

        public IReadOnlyList<FilePath> RelatedTraceFiles(FilePath session)
        {
            FilePath sub = session.Parent!.Value.Combine("sub");
            if (!Directory.Exists(sub.Value)) return [];
            return [.. Directory.EnumerateFiles(sub.Value).Select(p => new FilePath(p))];
        }

        /// <summary>A line "@5 text" carries the time 5 s; without the prefix, it carries no time.</summary>
        public ILineParser NewParser(FilePath f) => new FakeParser(f);
    }

    private sealed class FakeParser(FilePath file) : ILineParser
    {
        public IReadOnlyList<AgentEvent> Parse(string line, long lineNumber)
        {
            DateTimeOffset? ts = null;
            string text = line;
            if (line.StartsWith('@'))
            {
                int space = line.IndexOf(' ');
                ts = DateTimeOffset.FromUnixTimeSeconds(long.Parse(line[1..space], CultureInfo.InvariantCulture));
                text = line[(space + 1)..];
            }
            return [new UserMessage(new EventHeader(new SessionId("s"), "fake", null, ts, new SourceRef(file, lineNumber)), text)];
        }
    }

    private sealed class Recorder : ITraceFollowerListener
    {
        private readonly object _lock = new();
        private readonly List<FilePath> _sessions = [];
        private readonly List<string> _texts = [];
        private readonly List<Exception> _errors = [];

        public List<FilePath> Sessions { get { lock (_lock) return [.. _sessions]; } }

        public List<string> Texts { get { lock (_lock) return [.. _texts]; } }

        public List<Exception> Errors { get { lock (_lock) return [.. _errors]; } }

        public void SessionChanged(FilePath s)
        {
            lock (_lock) _sessions.Add(s);
        }

        public void Events(IReadOnlyList<AgentEvent> events)
        {
            lock (_lock)
            {
                foreach (AgentEvent e in events) _texts.Add(((UserMessage)e).Text);
            }
        }

        public void Error(Exception error)
        {
            lock (_lock) _errors.Add(error);
        }
    }

    private TraceFollower Follower(Recorder r) =>
        new(new FakeAdapter(), Dir, TimeSpan.FromMilliseconds(10), TimeSpan.Zero, r);

    private static FilePath Write(FilePath f, string content, long mtimeMillis)
    {
        Directory.CreateDirectory(f.Parent!.Value.Value);
        File.WriteAllText(f.Value, content);
        File.SetLastWriteTimeUtc(f.Value, DateTimeOffset.FromUnixTimeMilliseconds(mtimeMillis).UtcDateTime);
        return f;
    }

    [Fact]
    public void DoesNothingWhileThereAreNoSessions()
    {
        var r = new Recorder();
        Follower(r).Tick();
        Assert.Empty(r.Sessions);
        Assert.Empty(r.Texts);
    }

    [Fact]
    public void FollowsTheNewestSessionWithItsRelatedTraces()
    {
        Write(Dir.Combine("old.jsonl"), "old\n", 1_000);
        FilePath current = Write(Dir.Combine("current.jsonl"), "one\ntwo\n", 2_000);
        Write(Dir.Combine("sub", "agent-1.jsonl"), "sub-one\n", 1_500);
        var r = new Recorder();
        TraceFollower f = Follower(r);
        f.Tick();
        Assert.Equal([current], r.Sessions);
        Assert.Equal(["one", "two", "sub-one"], r.Texts);

        File.AppendAllText(current.Value, "three\n");
        f.Tick();
        Assert.Equal(["one", "two", "sub-one", "three"], r.Texts);
    }

    [Fact]
    public void MergesSessionAndSubagentEventsInTimeOrder()
    {
        Write(Dir.Combine("s.jsonl"), "@10 main-1\nno-time\n@30 main-2\n", 2_000);
        Write(Dir.Combine("sub", "agent-1.jsonl"), "@20 sub-1\n@25 sub-2\n", 1_500);
        var r = new Recorder();
        Follower(r).Tick();
        // "no-time" inherits the time of the previous line of its file and stays right behind it
        Assert.Equal(["main-1", "no-time", "sub-1", "sub-2", "main-2"], r.Texts);
    }

    [Fact]
    public void SwitchesToANewerSessionAndDropsTheOldOne()
    {
        FilePath first = Write(Dir.Combine("a.jsonl"), "a1\n", 1_000);
        var r = new Recorder();
        TraceFollower f = Follower(r);
        f.Tick();
        FilePath second = Write(Dir.Combine("b.jsonl"), "b1\n", 2_000);
        f.Tick();
        Assert.Equal([first, second], r.Sessions);
        File.AppendAllText(first.Value, "a2\n");
        File.SetLastWriteTimeUtc(first.Value, DateTimeOffset.FromUnixTimeMilliseconds(1_000).UtcDateTime);
        f.Tick();
        Assert.Equal(["a1", "b1"], r.Texts);
    }

    [Fact]
    public void StartAndDisposeRunTheLoopOnATask()
    {
        Write(Dir.Combine("s.jsonl"), "x\n", 1_000);
        var r = new Recorder();
        TraceFollower f = Follower(r);
        f.Start();
        Eventually.Until("first event", () => r.Texts.Count > 0);
        f.Dispose();
        Assert.Equal(["x"], r.Texts);
        Assert.Empty(r.Errors);
    }
}
