using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Knowledge;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>e2e of the 3D knowledge graph with a real session: coherent turns, finite projection and the time of one frame.</summary>
[Trait("Category", "Acceptance")]
public sealed class KnowledgeGraph3DIT
{
    [Fact]
    public void ARealSessionProjectsToFiniteSceneAndAFrameIsCheap()
    {
        RealTrace.Found real = RealTrace.LocateOrSkip();
        var builder = new KnowledgeBuilder(null);
        foreach (AgentEvent e in SessionReader.ReadSession(real.Adapter, real.Session)) builder.Accept(e);
        KnowledgeGraph graph = builder.Snapshot();
        Assert.All(graph.Nodes, n => Assert.True(0 <= n.FirstTurn && n.FirstTurn <= n.LastTurn && n.LastTurn < graph.Turns, n.Path.Value));

        KnowledgeView view = KnowledgeView.Of(graph, false, KnowledgeView.MaxNodes);
        Dictionary<FilePath, ForceLayout.Point> flat = ForceLayout.Run(view, new Dictionary<FilePath, ForceLayout.Point>(), 150);
        Dictionary<FilePath, Point3> placed = Scene3D.Place(view, flat, graph.Turns);
        (Point3 min, Point3 max) = Scene3D.Bounds(placed.Values);
        var cam = new Camera3D();
        cam.FreeView();
        cam.Fit(min, max, 1200, 700);

        var frames = Stopwatch.StartNew();
        const int Count = 200;
        List<Scene3D.Item> items = [];
        for (int i = 0; i < Count; i++)
        {
            cam.Orbit(0.02, 0);
            items = Scene3D.Project(view, placed, cam, 1200, 700);
        }
        frames.Stop();
        Assert.All(items, i => Assert.True(double.IsFinite(i.X) && double.IsFinite(i.Y) && double.IsFinite(i.Radius)));
        double perFrame = frames.Elapsed.TotalMilliseconds / Count;
        Console.WriteLine($"[REAL3D] {view.Nodes.Count} nodes, {view.Edges.Count} edges, {graph.Turns} turns: projection {perFrame:0.000} ms per frame");
        Assert.True(perFrame < 16, "projecting one frame took " + perFrame + " ms");
    }
}
