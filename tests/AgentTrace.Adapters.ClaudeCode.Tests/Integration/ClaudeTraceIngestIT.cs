using AgentTrace.Core;
using AgentTrace.Ingest;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// End-to-end test of the full chain without UI: locator → adapter → tailer, with real threads and a fake
/// ~/.claude directory that is written to the way the agent would.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ClaudeTraceIngestIT : TempDirTest
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    private sealed class Listener : ITraceFollowerListener
    {
        private readonly object _lock = new();
        private readonly List<AgentEvent> _received = [];
        private readonly List<FilePath> _sessions = [];

        public List<AgentEvent> Received { get { lock (_lock) return [.. _received]; } }

        public List<FilePath> Sessions { get { lock (_lock) return [.. _sessions]; } }

        public void SessionChanged(FilePath s)
        {
            lock (_lock) _sessions.Add(s);
        }

        public void Events(IReadOnlyList<AgentEvent> e)
        {
            lock (_lock) _received.AddRange(e);
        }
    }

    [Fact]
    public void FollowsALiveClaudeSession()
    {
        FilePath root = Tmp.Combine("projects");
        FilePath project = Tmp.Combine("project");
        Directory.CreateDirectory(project.Value);
        FilePath folder = root.Combine(ClaudeProjectsLocator.Encode(project.Value));
        Directory.CreateDirectory(folder.Value);
        FilePath session = folder.Combine(Session + ".jsonl");
        File.Copy(TestSupport.Resource("claude/projects/D--fake-proj/" + Session + ".jsonl").Value, session.Value);
        FilePath subDir = folder.Combine(Session, "subagents");
        Directory.CreateDirectory(subDir.Value);
        File.Copy(TestSupport.Resource("claude/projects/D--fake-proj/" + Session + "/subagents/agent-abc123.jsonl").Value,
            subDir.Combine("agent-abc123.jsonl").Value);

        var adapter = new ClaudeCodeAdapter(root, false);
        IReadOnlyList<AgentEvent> expected = SessionReader.ReadSession(adapter, session);

        var listener = new Listener();
        using var follower = new TraceFollower(adapter, project, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(60), listener);
        follower.Start();

        // 1. Initial read of the session and its subagent
        Eventually.Until("initial read", () => listener.Received.Count == expected.Count, 5_000, 20);
        Assert.Equal([session], listener.Sessions);
        var stats = new SessionStats();
        foreach (AgentEvent e in listener.Received) stats.Accept(e);
        Assert.True(stats.TotalToolCalls >= 5);
        Assert.Equal(2, stats.ToolErrors); // the Bash error and the Read of a file that does not exist
        Assert.Equal(2, stats.FilesRead.Count); // the two successful reads (text and image); the failed one does not count
        Assert.Equal(2, stats.FilesTouched.Count);
        Assert.Single(stats.FilesChangedExternally);
        Assert.Contains(listener.Received, e => e.Header.SubagentId == "abc123");

        // 2. Partial line: it is not interpreted until it is completed
        int before = listener.Received.Count;
        string start = "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"message ";
        File.AppendAllText(session.Value, start);
        Thread.Sleep(300);
        Assert.Equal(before, listener.Received.Count);
        File.AppendAllText(session.Value, "new\"},\"sessionId\":\"" + Session + "\"}\n");
        Eventually.Until("line completed", () => listener.Received.Count == before + 1, 5_000, 20);
        Assert.Equal("message new", ((UserMessage)listener.Received[^1]).Text);
        Assert.DoesNotContain(listener.Received, e => e.Header.Source.LineNumber == 0);

        // 3. Another session starts: it switches to it
        FilePath second = folder.Combine("bbbbbbbb-0000-0000-0000-000000000002.jsonl");
        File.WriteAllText(second.Value, "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"second session\"}}\n");
        File.SetLastWriteTimeUtc(second.Value, DateTime.UtcNow.AddSeconds(60));
        Eventually.Until("session switch", () => listener.Sessions.Count == 2, 5_000, 20);
        Assert.Equal(second, listener.Sessions[1]);
        Eventually.Until("events of the second session",
            () => listener.Received.Any(e => e is UserMessage { Text: "second session" }), 5_000, 20);
    }

    private sealed class FailingListener : ITraceFollowerListener
    {
        public void SessionChanged(FilePath s) => throw new InvalidOperationException("no sessions");

        public void Events(IReadOnlyList<AgentEvent> e) => throw new InvalidOperationException("no events");
    }

    [Fact]
    public void ReadingNeverCreatesFilesInTheAgentFolder()
    {
        FilePath root = Tmp.Combine("projects");
        Directory.CreateDirectory(root.Value);
        FilePath project = Tmp.Combine("no-traces");
        Directory.CreateDirectory(project.Value);
        var adapter = new ClaudeCodeAdapter(root, false);
        using (var follower = new TraceFollower(adapter, project, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), new FailingListener()))
        {
            follower.Start();
            Thread.Sleep(250);
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Value));
    }
}
