using System.Text;
using AgentTrace.Core;
using AgentTrace.Git;
using AgentTrace.Presentation.Observation;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// e2e of the whole change chain: files are written into a real git repository while it is observed; then the
/// Claude Code trace (in a fake ~/.claude) says which tools did it. Checks the live list, the notice
/// in under 3 s and exact, probable and no attribution.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GitChangesIT : TempDirTest
{
    private const string SessionId = "bbbbbbbb-0000-0000-0000-000000000009";

    private sealed class Recorder : IProjectObserverListener
    {
        private readonly object _lock = new();
        private readonly List<IReadOnlyList<ChangeEntry>> _updates = [];
        private readonly List<GitState> _states = [];

        public List<GitState> States { get { lock (_lock) return [.. _states]; } }

        public ChangeEntry? Find(string name)
        {
            lock (_lock)
            {
                return _updates.Count == 0 ? null : _updates[^1].FirstOrDefault(e => e.Change.Path.FileName == name);
            }
        }

        public void SessionChanged(string agent, FilePath sessionTrace)
        {
        }

        public void Events(IReadOnlyList<AgentEvent> events)
        {
        }

        public void Changes(IReadOnlyList<ChangeEntry> changes)
        {
            lock (_lock) _updates.Add(changes);
        }

        public void GitStateChanged(GitState state, string detail)
        {
            lock (_lock) _states.Add(state);
        }

        public void Error(string message) => throw new InvalidOperationException(message);
    }

    private static string Line(string json) => json + "\n";

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string BashCall(string id, string message, DateTimeOffset at) =>
        Line("{\"type\":\"assistant\",\"timestamp\":\"" + Iso(at) + "\",\"sessionId\":\"" + SessionId + "\",\"message\":{\"id\":\"" + message
            + "\",\"model\":\"m\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id
            + "\",\"name\":\"Bash\",\"input\":{\"command\":\"echo hello > bash-writes.txt\"}}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}}");

    private static string ToolResult(string id, string extra, DateTimeOffset at) =>
        Line("{\"type\":\"user\",\"timestamp\":\"" + Iso(at) + "\",\"sessionId\":\"" + SessionId + "\",\"message\":{\"role\":\"user\","
            + "\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id + "\",\"content\":\"ok\"}]}" + extra + "}");

    private static string WriteCall(string id, string message, string file, DateTimeOffset at) =>
        Line("{\"type\":\"assistant\",\"timestamp\":\"" + Iso(at) + "\",\"sessionId\":\"" + SessionId + "\",\"message\":{\"id\":\"" + message
            + "\",\"model\":\"m\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id
            + "\",\"name\":\"Write\",\"input\":{\"file_path\":" + Quote(file) + "}}],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}}");

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private void AwaitOrFail(Recorder r, string what, Func<ChangeEntry, bool> predicate, string file)
    {
        Eventually.Until(what + " (" + file + ")", () => r.Find(file) is { } e && predicate(e), 10_000, 25);
    }

    [Fact]
    public void FollowsRealChangesAndAttributesThemWhenTheTraceArrives()
    {
        Assert.SkipUnless(GitFixture.GitAvailable(), "This test needs git installed");

        // Real repository with one commit, and a Claude Code trace that for now only has a user message
        GitFixture repo = GitFixture.Init(Tmp.Combine("project"));
        repo.Write("base.txt", "base\n");
        repo.CommitAll("base");
        FilePath root = Tmp.Combine("projects");
        FilePath folder = root.Combine(ClaudeProjectsLocator.Encode(repo.Dir.Value));
        Directory.CreateDirectory(folder.Value);
        FilePath trace = folder.Combine(SessionId + ".jsonl");
        File.WriteAllText(trace.Value, Line("{\"type\":\"user\",\"timestamp\":\"" + Iso(DateTimeOffset.UtcNow) + "\",\"sessionId\":\"" + SessionId
            + "\",\"message\":{\"role\":\"user\",\"content\":\"create some files\"}}"));

        var r = new Recorder();
        var adapter = new ClaudeCodeAdapter(root, false);
        using var observer = new ProjectObserver(repo.Dir, [adapter], GitCli.ForDirectory(repo.Dir), TimeSpan.FromMilliseconds(300),
            TimeSpan.FromSeconds(2), r);
        observer.Start();
        Eventually.Until("the repository did not become ready", () => r.States.Contains(GitState.Ready), 10_000, 25);

        // 1. Two files are written: they appear live, for now without attribution
        DateTimeOffset before = DateTimeOffset.UtcNow;
        File.WriteAllText(repo.Dir.Combine("bash-writes.txt").Value, "hello\n");
        File.WriteAllText(repo.Dir.Combine("write-tool.txt").Value, "content\n");
        AwaitOrFail(r, "the new file appears in the list", _ => true, "write-tool.txt");
        TimeSpan latency = DateTimeOffset.UtcNow - before;
        AwaitOrFail(r, "the other file too", _ => true, "bash-writes.txt");
        Assert.Equal(ChangeKind.Created, r.Find("bash-writes.txt")!.Change.Kind);
        Assert.Equal(Confidence.None, r.Find("bash-writes.txt")!.Attribution.Confidence); // with no trace explaining it, the change is external
        Assert.True(latency.TotalSeconds < 3 + 2, "took " + latency + " to appear");

        // 2. The trace arrives: a Bash call surrounding the first write and a Write declaring the second
        // (both files were written less than a second apart: their times are separated
        // so that each trace call explains a single file)
        DateTimeOffset baseTime = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(repo.Dir.Combine("bash-writes.txt").Value, baseTime.UtcDateTime);
        File.SetLastWriteTimeUtc(repo.Dir.Combine("write-tool.txt").Value, baseTime.AddSeconds(60).UtcDateTime);
        DateTimeOffset bashAt = new(File.GetLastWriteTimeUtc(repo.Dir.Combine("bash-writes.txt").Value), TimeSpan.Zero);
        DateTimeOffset writeAt = new(File.GetLastWriteTimeUtc(repo.Dir.Combine("write-tool.txt").Value), TimeSpan.Zero);
        FilePath writtenByTool = r.Find("write-tool.txt")!.Change.Path;
        string lines = BashCall("toolu_bash", "msg_b", bashAt.AddSeconds(-1))
            + ToolResult("toolu_bash", "", bashAt.AddSeconds(1))
            + WriteCall("toolu_write", "msg_w", writtenByTool.Value, writeAt.AddMilliseconds(-500))
            + ToolResult("toolu_write", ",\"toolUseResult\":{\"type\":\"create\",\"filePath\":" + Quote(writtenByTool.Value)
                + ",\"originalFile\":null}", writeAt.AddMilliseconds(100));
        File.AppendAllText(trace.Value, lines, new UTF8Encoding(false));

        AwaitOrFail(r, "the Bash call is attributed as probable", e => e.Attribution.Confidence == Confidence.Probable, "bash-writes.txt");
        ToolCall bash = r.Find("bash-writes.txt")!.Attribution.Calls[0];
        Assert.Equal("Bash", bash.ToolName);
        Assert.Contains("echo hello", bash.InputJson);

        AwaitOrFail(r, "the Write call is attributed as exact", e => e.Attribution.Confidence == Confidence.Exact, "write-tool.txt");
        Assert.Equal("Write", r.Find("write-tool.txt")!.Attribution.Calls[0].ToolName);

        // 3. A change whose time matches no call stays without attribution
        FilePath old = repo.Write("old.txt", "from another moment\n");
        File.SetLastWriteTimeUtc(old.Value, bashAt.AddSeconds(-3600).UtcDateTime);
        AwaitOrFail(r, "the foreign change appears", _ => true, "old.txt");
        Assert.Equal(Confidence.None, r.Find("old.txt")!.Attribution.Confidence);

        // Comparing against the commit did not touch the repository nor leave anything hanging
        Assert.False(File.Exists(repo.Dir.Combine(".git", "index.lock").Value));
    }
}
