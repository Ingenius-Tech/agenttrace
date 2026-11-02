using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// Acceptance of the graph with a REAL Claude Code trace (the one <see cref="RealClaudeTraceIT"/> uses): no call is
/// lost, each result is linked to its call and the layout is coherent. Skipped without a trace.
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class RealFlowGraphIT
{
    [Fact]
    public void ARealSessionBecomesACoherentGraph()
    {
        RealTrace.Found real = RealTrace.LocateOrSkip();
        IReadOnlyList<AgentEvent> events = SessionReader.ReadSession(real.Adapter, real.Session);
        var builder = new FlowBuilder();
        foreach (AgentEvent e in events) builder.Accept(e);
        FlowGraph graph = builder.Snapshot();

        // Each call of the trace is a step, and each result with a known call sets its state
        HashSet<string> calls = [.. events.OfType<ToolCall>().Select(c => c.CallId)];
        Assert.Equal(calls.Count, graph.StepCount); // steps = distinct calls
        HashSet<string> failed = [.. events.OfType<ToolResult>().Where(r => r.Error && calls.Contains(r.CallId)).Select(r => r.CallId)];
        Assert.Equal(failed.Count, graph.ErrorCount);
        foreach (string id in failed) Assert.Equal(FlowGraph.StepState.Error, graph.FindStep(id)!.State);
        Assert.Equal(events.OfType<ToolResult>().Count(r => !calls.Contains(r.CallId)), graph.OrphanResults);

        // A batch does not mix lanes or turns, and each subagent lane matches a thread of the trace
        foreach (FlowGraph.Batch b in graph.Batches)
        {
            Assert.NotEmpty(b.Steps);
            Assert.True(b.TurnIndex >= 0 && b.TurnIndex < Math.Max(graph.Turns.Count, 1), "turn of the batch " + b.TurnIndex);
        }
        HashSet<string> threads = [.. events.Select(e => e.Header.SubagentId).OfType<string>()];
        Assert.Equal(threads.Count, graph.Lanes.Count - 1);
        Assert.True(graph.Turns.Count(t => t.Prompt is not null) >= 1);

        // Layout: one box per step, no overlaps and all reachable by click
        long start = Stopwatch.GetTimestamp();
        GraphGeometry geometry = FlowLayout.Of(graph);
        long millis = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Assert.Equal(graph.StepCount, geometry.Boxes.Count(b => b.Kind == GraphGeometry.Kind.Step));
        foreach (GraphGeometry.Box a in geometry.Boxes) Assert.Equal(a, geometry.Hit(a.X + a.W / 2.0, a.CenterY));

        // The boxes are ordered by column: only those of the same or the next column can overlap
        IReadOnlyList<GraphGeometry.Box> boxes = geometry.Boxes;
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count && boxes[j].X < boxes[i].Right; j++)
            {
                GraphGeometry.Box a = boxes[i];
                GraphGeometry.Box b = boxes[j];
                Assert.False(a.Y < b.Bottom && b.Y < a.Bottom, "overlap " + a + " / " + b);
            }
        }

        // The same session with time going down: another box per step, no overlaps and all reachable by click
        GraphGeometry vertical = FlowLayout.Of(graph, new HashSet<int>(), GraphOrientation.Vertical);
        Assert.Equal(boxes.Count, vertical.Boxes.Count);
        IReadOnlyList<GraphGeometry.Box> vboxes = vertical.Boxes;
        for (int i = 0; i < vboxes.Count; i++)
        {
            GraphGeometry.Box a = vboxes[i];
            Assert.Equal(a, vertical.Hit(a.CenterX, a.CenterY));
            for (int j = i + 1; j < vboxes.Count && vboxes[j].Y < a.Bottom; j++)
            {
                GraphGeometry.Box b = vboxes[j];
                Assert.False(a.X < b.Right && b.X < a.Right, "vertical overlap " + a + " / " + b);
            }
        }
        Console.WriteLine("RealFlowGraphIT: " + graph.StepCount + " steps, " + graph.Batches.Count + " batches, "
            + graph.Turns.Count + " turns, " + (graph.Lanes.Count - 1) + " subagents; layout in " + millis + " ms");
        Assert.True(millis < 2_000, "the layout took " + millis + " ms");
    }
}
