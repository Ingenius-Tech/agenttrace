using AgentTrace.Core;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Presentation.Knowledge;

/// <summary>
/// The knowledge graph in three dimensions: X and Y are those of the 2D layout and Z is time (the turn in which each file
/// first appears, in layers). No forces are simulated in Z, so Z only means time. Pure: no Avalonia.
/// </summary>
public static class Scene3D
{
    /// <summary>Maximum on-screen radius: with few nodes, the fit brings the camera so close that each one would be huge.</summary>
    public const double MaxRadius = 26;

    /// <summary>Spacing between layers: the total height is about half the width of the layout (between 4 and 60 per turn).</summary>
    public static double LayerGap(IReadOnlyDictionary<FilePath, ForceLayout.Point> positions, int turns)
    {
        if (positions.Count == 0 || turns <= 1) return 20;
        double width = positions.Values.Max(p => p.X) - positions.Values.Min(p => p.X);
        double height = positions.Values.Max(p => p.Y) - positions.Values.Min(p => p.Y);
        return Math.Clamp(Math.Max(width, height) * 0.5 / (turns - 1), 4, 60);
    }

    /// <summary>Places each visible node: XY from the 2D layout, Z = first turn × spacing.</summary>
    public static Dictionary<FilePath, Point3> Place(KnowledgeView view, IReadOnlyDictionary<FilePath, ForceLayout.Point> positions, int turns)
    {
        double gap = LayerGap(positions, turns);
        var output = new Dictionary<FilePath, Point3>();
        foreach (Node n in view.Nodes)
        {
            if (positions.TryGetValue(n.Path, out ForceLayout.Point? p)) output[n.Path] = new Point3(p.X, p.Y, Math.Max(n.FirstTurn, 0) * gap);
        }
        return output;
    }

    /// <summary>A file touched again after appearing: <c>From</c> is at its first turn (where the node is drawn) and <c>To</c> at the last.</summary>
    public sealed record Trail(Node Node, Point3 From, Point3 To);

    /// <summary>
    /// Trail of each visible node touched in more than one turn, from its first to its last turn. Z is still the first turn
    /// (the layout does not move); the trail shows which old files are still active.
    /// </summary>
    public static List<Trail> Trails(KnowledgeView view, IReadOnlyDictionary<FilePath, Point3> placed, double layerGap)
    {
        var output = new List<Trail>();
        foreach (Node n in view.Nodes)
        {
            if (n.LastTurn <= n.FirstTurn || n.FirstTurn < 0 || !placed.TryGetValue(n.Path, out Point3? from)) continue;
            output.Add(new Trail(n, from, new Point3(from.X, from.Y, n.LastTurn * layerGap)));
        }
        return output;
    }

    public static (Point3 Min, Point3 Max) Bounds(IReadOnlyCollection<Point3> points) => points.Count == 0
        ? (new Point3(0, 0, 0), new Point3(1, 1, 1))
        : (new Point3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
           new Point3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));

    /// <summary>A projected node: <c>Radius</c> in pixels and <c>Fog</c> from 0 (nearest) to 1 (farthest).</summary>
    public sealed record Item(Node Node, double X, double Y, double Depth, double Radius, double Fog);

    /// <summary>Projects the visible nodes and returns them from far to near (painter's algorithm).</summary>
    public static List<Item> Project(KnowledgeView view, IReadOnlyDictionary<FilePath, Point3> placed, Camera3D camera, double width, double height)
    {
        var items = new List<(Node Node, Projected P)>();
        foreach (Node n in view.Nodes)
        {
            if (!placed.TryGetValue(n.Path, out Point3? q)) continue;
            Projected p = camera.Project(q.X, q.Y, q.Z, width, height);
            if (p.Visible) items.Add((n, p));
        }
        if (items.Count == 0) return [];
        double near = items.Min(i => i.P.Depth);
        double far = items.Max(i => i.P.Depth);
        double span = Math.Max(far - near, 1e-9);
        return items.OrderByDescending(i => i.P.Depth)
            .Select(i => new Item(i.Node, i.P.X, i.P.Y, i.P.Depth, Math.Clamp(KnowledgeView.Radius(i.Node) * i.P.Scale, 2, MaxRadius), (i.P.Depth - near) / span))
            .ToList();
    }

    /// <summary>The node under the screen point; where they overlap the nearest wins. The items come from far to near.</summary>
    public static Node? HitTest(IReadOnlyList<Item> items, double sx, double sy, double slack = 3)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            Item it = items[i];
            double d = Math.Sqrt((it.X - sx) * (it.X - sx) + (it.Y - sy) * (it.Y - sy));
            if (d <= it.Radius + slack) return it.Node;
        }
        return null;
    }
}
