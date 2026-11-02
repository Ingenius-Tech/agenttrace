using AgentTrace.Core;
using Rect = AgentTrace.Presentation.Graph.GraphGeometry.Rect;

namespace AgentTrace.Presentation.Heatmap;

/// <summary>What decides the area of a cell: size on disk or activity (what was not touched disappears).</summary>
public enum HeatArea
{
    Size,
    Activity,
}

/// <summary>
/// A cell of the map. <c>Depth</c>: 0 at the root of the view. <c>HeaderHeight</c>: height of the header with the name
/// (only folders with room for it; 0 otherwise). The children of a folder go in cells after its own.
/// </summary>
public sealed record TreemapCell(HeatNode Node, Rect Bounds, int Depth, double HeaderHeight);

/// <summary>
/// Layout of the map inside a rectangle: no Avalonia, no window state; recomputed only if data,
/// size, zoom, area or filters change. Cells are in paint order (each folder before its contents).
/// </summary>
public sealed class TreemapLayout
{
    public const double HeaderHeight = 16;
    public const double Margin = 2;
    public const double MinCell = 3;
    private const double MinHeaderWidth = 48;
    private const double MinHeaderHeight = 32;

    private TreemapLayout(HeatNode zoom, IReadOnlyList<TreemapCell> cells)
    {
        Zoom = zoom;
        Cells = cells;
    }

    /// <summary>The node that fills the view.</summary>
    public HeatNode Zoom { get; }

    public IReadOnlyList<TreemapCell> Cells { get; }

    /// <summary>The highest heat of a cell in the view (for the color scale).</summary>
    public double MaxHeat(ActivityFilter filter) => Cells.Count == 0 ? 0 : Cells.Max(c => c.Node.Activity.Value(filter));

    /// <summary>
    /// Lays out <paramref name="zoom"/> and its content in <paramref name="width"/> × <paramref name="height"/> pixels. Children go from most
    /// to least weight and, at equal weight, by ordinal path; cells under 3 px are not drawn (their folder absorbs them).
    /// </summary>
    public static TreemapLayout Of(HeatNode zoom, double width, double height, HeatArea area, ActivityFilter filter)
    {
        List<TreemapCell> cells = [];
        if (width >= MinCell && height >= MinCell && Weight(zoom, area, filter) > 0)
        {
            Place(zoom, new Rect(0, 0, width, height), 0, area, filter, cells);
        }
        return new TreemapLayout(zoom, cells);
    }

    private static void Place(HeatNode node, Rect bounds, int depth, HeatArea area, ActivityFilter filter, List<TreemapCell> cells)
    {
        bool folder = node.Directory && node.Children.Count > 0;
        double header = folder && bounds.W >= MinHeaderWidth && bounds.H >= MinHeaderHeight ? HeaderHeight : 0;
        cells.Add(new TreemapCell(node, bounds, depth, header));
        if (!folder) return;

        var inner = new Rect(bounds.X + Margin, bounds.Y + Margin + header, bounds.W - 2 * Margin, bounds.H - 2 * Margin - header);
        if (inner.W < MinCell || inner.H < MinCell) return;
        List<HeatNode> children = [.. node.Children
            .Where(c => Weight(c, area, filter) > 0)
            .OrderByDescending(c => Weight(c, area, filter))
            .ThenBy(c => c.Path.Value, StringComparer.Ordinal)];
        IReadOnlyList<Rect> rects = Squarify.Layout([.. children.Select(c => Weight(c, area, filter))], inner);
        for (int i = 0; i < children.Count; i++)
        {
            if (rects[i].W >= MinCell && rects[i].H >= MinCell) Place(children[i], rects[i], depth + 1, area, filter, cells);
        }
    }

    private static double Weight(HeatNode node, HeatArea area, ActivityFilter filter) =>
        area == HeatArea.Size ? node.AreaBytes : node.Activity.Value(filter);

    /// <summary>The deepest cell containing the point (the header and margin of a folder belong to the folder), or null.</summary>
    public TreemapCell? HitTest(double x, double y)
    {
        for (int i = Cells.Count - 1; i >= 0; i--)
        {
            Rect b = Cells[i].Bounds;
            if (x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom) return Cells[i];
        }
        return null;
    }

    /// <summary>The folders from the project root to <paramref name="zoom"/> (included); only the root if it is not found.</summary>
    public static IReadOnlyList<HeatNode> Breadcrumb(HeatTree tree, FilePath zoom)
    {
        List<HeatNode> chain = [];
        for (FilePath? p = zoom; p is { } current && current.IsUnder(tree.Root.Path); p = current.Parent)
        {
            if (tree.Find(current) is not { } node) return [tree.Root];
            chain.Add(node);
            if (current == tree.Root.Path) break;
        }
        chain.Reverse();
        return chain.Count > 0 && chain[0] == tree.Root ? chain : [tree.Root];
    }
}
