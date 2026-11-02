using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Graph;
using AgentTrace.Testing;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// Headless e2e of the graph: Claude Code trace → adapter → <c>FlowBuilder</c> → <c>FlowLayout</c>, and the same
/// result when the trace arrives in chunks through the follower.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FlowGraphIT : TempDirTest
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    private static FilePath Resource(string name) => TestSupport.Resource("claude/projects/D--fake-proj/" + name);

    private static FilePath[] FakeSession(FilePath root, FilePath project)
    {
        FilePath folder = root.Combine(ClaudeProjectsLocator.Encode(project.Value));
        Directory.CreateDirectory(folder.Value);
        FilePath session = folder.Combine(Session + ".jsonl");
        File.Copy(Resource(Session + ".jsonl").Value, session.Value);
        FilePath subDir = folder.Combine(Session, "subagents");
        Directory.CreateDirectory(subDir.Value);
        FilePath sub = subDir.Combine("agent-abc123.jsonl");
        File.Copy(Resource(Session + "/subagents/agent-abc123.jsonl").Value, sub.Value);
        File.Copy(Resource(Session + "/subagents/agent-abc123.meta.json").Value, subDir.Combine("agent-abc123.meta.json").Value);
        return [session, sub];
    }

    private static FlowGraph Build(IEnumerable<AgentEvent> events)
    {
        var b = new FlowBuilder();
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }

    [Fact]
    public void TheSyntheticSessionBecomesAGraphWithItsSubagentParallelCallsAndErrors()
    {
        FilePath root = Tmp.Combine("projects");
        FilePath project = Tmp.Combine("project");
        Directory.CreateDirectory(project.Value);
        FilePath session = FakeSession(root, project)[0];
        FlowGraph graph = Build(SessionReader.ReadSession(new ClaudeCodeAdapter(root, false), session));

        // A subagent lane, linked to the call that launched it
        Assert.Equal(2, graph.Lanes.Count);
        FlowGraph.Lane lane = graph.Lanes[1];
        Assert.Equal("abc123", lane.Id);
        Assert.Equal("toolu_5", lane.ParentCallId);
        Assert.Equal("investigar", lane.Title); // lang-ok: fixture data (TestData)
        Assert.Equal("abc123", graph.FindStep("toolu_5")!.SubagentId);
        Assert.Equal("investiga el modulo", lane.Task); // lang-ok: fixture data. Its first message is the assignment
        Assert.Empty(graph.BatchesOf("abc123")); // the synthetic subagent only answers with text: without calls there are no batches

        // The batch of three calls launched at once
        FlowGraph.Batch parallel = graph.Batches.First(b => b.Steps.Any(s => s.CallId == "toolu_9a"));
        Assert.Equal(["toolu_9a", "toolu_9b", "toolu_9c"], parallel.Steps.Select(s => s.CallId));

        // The two errors of the trace, each with its state
        Assert.Equal(FlowGraph.StepState.Error, graph.FindStep("toolu_4")!.State);
        Assert.Equal(FlowGraph.StepState.Error, graph.FindStep("toolu_8")!.State);
        Assert.Equal(2, graph.ErrorCount);
        Assert.Equal(0, graph.OrphanResults);

        // There are user prompts and the later work lands in turns of its own
        Assert.True(graph.Turns.Count >= 2);
        Assert.Equal("hola agente", graph.Turns[0].Prompt); // lang-ok: fixture data
        Assert.True(graph.Turns.Any(t => t.Prompt is null) || graph.Turns.Any(t => !t.Closed));

        // And everything can be laid out: each step has its box, all reachable by click
        GraphGeometry geometry = FlowLayout.Of(graph);
        Assert.Equal(graph.StepCount, geometry.Boxes.Count(b => b.Kind == GraphGeometry.Kind.Step));
        foreach (GraphGeometry.Box box in geometry.Boxes) Assert.Equal(box, geometry.Hit(box.X + 1, box.Y + 1));
        Assert.Single(geometry.Rows); // a lane without batches takes no row
    }

    private sealed class Feeder(FlowBuilder builder) : ITraceFollowerListener
    {
        private readonly object _lock = new();
        private int _received;

        public int Received { get { lock (_lock) return _received; } }

        public FlowGraph Snapshot()
        {
            lock (builder) return builder.Snapshot();
        }

        public void SessionChanged(FilePath s)
        {
        }

        public void Events(IReadOnlyList<AgentEvent> e)
        {
            lock (builder)
            {
                foreach (AgentEvent evt in e) builder.Accept(evt);
            }
            lock (_lock) _received += e.Count;
        }
    }

    [Fact]
    public void FeedingTheTraceInChunksThroughTheFollowerGivesTheSameGraphAsReadingItAtOnce()
    {
        FilePath root = Tmp.Combine("projects");
        FilePath project = Tmp.Combine("project");
        Directory.CreateDirectory(project.Value);
        FilePath[] files = FakeSession(root, project);
        var adapter = new ClaudeCodeAdapter(root, false);
        FlowGraph expected = Build(SessionReader.ReadSession(adapter, files[0]));
        string[] lines = File.ReadAllLines(files[0].Value);

        // The same content, but arriving a few lines at a time in a new session
        FilePath liveRoot = Tmp.Combine("live");
        FilePath liveProject = Tmp.Combine("liveproject");
        Directory.CreateDirectory(liveProject.Value);
        FilePath[] live = FakeSession(liveRoot, liveProject);
        File.WriteAllText(live[0].Value, "");
        var liveAdapter = new ClaudeCodeAdapter(liveRoot, false);

        var feeder = new Feeder(new FlowBuilder());
        using var follower = new TraceFollower(liveAdapter, liveProject, TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50), feeder);
        follower.Start();
        for (int i = 0; i < lines.Length; i += 4)
        {
            string chunk = string.Join("\n", lines.Skip(i).Take(4)) + "\n";
            File.AppendAllText(live[0].Value, chunk);
            Thread.Sleep(80);
        }
        int expectedEvents = SessionReader.ReadSession(adapter, files[0]).Count;
        Eventually.Until("all the events", () => feeder.Received >= expectedEvents, 8_000, 25);
        FlowGraph actual = feeder.Snapshot();
        Assert.Equal(expected.StepCount, actual.StepCount);
        Assert.Equal(expected.Turns.Select(t => t.Prompt), actual.Turns.Select(t => t.Prompt));
        Assert.Equal(expected.Batches.Select(b => b.Steps.Select(s => s.CallId).ToList()),
            actual.Batches.Select(b => b.Steps.Select(s => s.CallId).ToList()));
        Assert.Equal(expected.Lanes.Count, actual.Lanes.Count);
        Assert.Equal(expected.ErrorCount, actual.ErrorCount);
    }
}
