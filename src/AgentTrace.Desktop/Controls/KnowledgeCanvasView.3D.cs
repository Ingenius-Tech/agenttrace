using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Presentation.Resources;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// The 3D view of the canvas: X and Y are the 2D layout and Z is time (layers per turn). The camera, the projection and the
/// depth-based selection are pure logic (<c>Scene3D</c>, <c>Camera3D</c>); here we only paint.
/// </summary>
public sealed partial class KnowledgeCanvasView
{
    private readonly Camera3D _camera = new();
    private Dictionary<FilePath, Point3> _placed = [];
    private List<Scene3D.Item> _items = [];
    private bool _mode3D;
    private bool _fitted3D;
    private bool _showTrails = true;

    public bool Mode3D => _mode3D;

    public Camera3D Camera => _camera;

    public void SetMode3D(bool value)
    {
        if (_mode3D == value) return;
        _mode3D = value;
        if (value) FitOnce3D();
        InvalidateVisual();
    }

    /// <summary>Shows or hides the vertical trail (first to last turn in which each file was touched).</summary>
    public void SetTrails(bool value)
    {
        if (_showTrails == value) return;
        _showTrails = value;
        InvalidateVisual();
    }

    public bool ShowTrails => _showTrails;

    /// <summary>Preset views: <c>top</c> (same as 2D), <c>side</c> (time vertical) and <c>free</c>.</summary>
    public void SetPreset(string name)
    {
        switch (name)
        {
            case "top": _camera.TopView(); break;
            case "side": _camera.SideView(); break;
            default: _camera.FreeView(); break;
        }
        InvalidateVisual();
    }

    private void Fit3D()
    {
        if (_placed.Count == 0) return;
        (Point3 min, Point3 max) = Scene3D.Bounds(_placed.Values);
        _camera.Fit(min, max, Bounds.Width, Bounds.Height);
        _fitted3D = true;
        InvalidateVisual();
    }

    private void FitOnce3D()
    {
        if (_fitted3D || _placed.Count == 0 || Bounds.Width <= 0) return;
        Fit3D();
    }

    private void Render3D(DrawingContext ctx, HashSet<FilePath> neighbours)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        _items = Scene3D.Project(_shown, _placed, _camera, w, h);
        var byPath = _items.ToDictionary(i => i.Node.Path);
        PaintTimeAxis(ctx, w, h);
        double crowded = _shown.Edges.Count > 800 ? 0.12 : _shown.Edges.Count > 250 ? 0.3 : 0.6;
        foreach (Edge e in _shown.Edges)
        {
            if (!byPath.TryGetValue(e.From, out Scene3D.Item? a) || !byPath.TryGetValue(e.To, out Scene3D.Item? b)) continue;
            bool related = _selected is { } s && (e.From == s || e.To == s);
            double fog = 1 - 0.65 * Math.Min(a.Fog, b.Fog);
            double alpha = (_selected is null ? crowded : related ? 0.9 : 0.05) * fog;
            double width = (1 + Math.Log(e.Weight)) * (related ? 1.6 : 1);
            using (ctx.PushOpacity(alpha))
            {
                Color color = e.Relation == Relation.CoChanged ? (related ? AppColors.Text : AppColors.Border) : AppColors.Read;
                DashStyle? dash = e.Relation == Relation.CoChanged ? null : new DashStyle([5, 4], 0);
                ctx.DrawLine(new Pen(AppColors.Brush(color), width, dash, PenLineCap.Round), new Point(a.X, a.Y), new Point(b.X, b.Y));
            }
        }
        if (_showTrails) PaintTrails3D(ctx, byPath, w, h);
        foreach (Scene3D.Item it in _items) PaintNode3D(ctx, it, neighbours);
    }

    /// <summary>Line from the first to the last turn of each file touched again, with a mark at the last one (behind the nodes).</summary>
    private void PaintTrails3D(DrawingContext ctx, Dictionary<FilePath, Scene3D.Item> byPath, double w, double h)
    {
        double gap = Scene3D.LayerGap(_positions, _graph.Turns);
        foreach (Scene3D.Trail t in Scene3D.Trails(_shown, _placed, gap))
        {
            if (!byPath.TryGetValue(t.Node.Path, out Scene3D.Item? it)) continue;
            Projected top = _camera.Project(t.To.X, t.To.Y, t.To.Z, w, h);
            if (!top.Visible) continue;
            bool focus = _selected is null || t.Node.Path == _selected;
            using (ctx.PushOpacity((focus ? 0.8 : 0.15) * (1 - 0.55 * it.Fog)))
            {
                Color color = AppColors.Status(t.Node.LastChange);
                ctx.DrawLine(new Pen(AppColors.Brush(color), 2, null, PenLineCap.Round), new Point(it.X, it.Y), new Point(top.X, top.Y));
                ctx.DrawEllipse(AppColors.Brush(color), new Pen(AppColors.Brush(AppColors.Background), 1), new Point(top.X, top.Y), Math.Max(3, it.Radius * 0.35), Math.Max(3, it.Radius * 0.35));
            }
        }
    }

    private void PaintNode3D(DrawingContext ctx, Scene3D.Item it, HashSet<FilePath> neighbours)
    {
        Node n = it.Node;
        bool matches = _query.Length == 0 || n.Path.FileName.ToLowerInvariant().Contains(_query, StringComparison.Ordinal);
        bool inFocus = _selected is null || neighbours.Contains(n.Path);
        double fog = 1 - 0.55 * it.Fog;
        using (ctx.PushOpacity((matches && inFocus ? 1 : matches ? 0.4 : Dim) * fog))
        {
            var center = new Point(it.X, it.Y);
            Pen outline = n.OnlyExternal
                ? new Pen(AppColors.Brush(AppColors.Text), 2, new DashStyle([1.5, 1.25], 0))
                : new Pen(AppColors.Brush(AppColors.Background), 1.5);
            ctx.DrawEllipse(AppColors.Brush(AppColors.Status(n.LastChange)), outline, center, it.Radius, it.Radius);
            if (it.Radius >= 7)
            {
                if (n.LastChange is not { } kind) DrawIcon(ctx, Presentation.Text.Codicon.Eye, it.X, it.Y, it.Radius * 1.2, AppColors.BadgeText);
                else DrawCentered(ctx, Presentation.Text.ChangeStyle.Symbol(kind), it.X, it.Y, it.Radius * 1.3, AppColors.BadgeText);
            }
            if (n.Path == _selected || n.Path == _hovered)
            {
                var pen = new Pen(AppColors.Brush(AppColors.Select), n.Path == _selected ? 3 : 1.5);
                ctx.DrawEllipse(null, pen, center, it.Radius + 3, it.Radius + 3);
            }
            // Labels only for what is near, the selection and its surroundings: in perspective, all of them would be a blur
            bool label = it.Fog < 0.25 || n.Path == _selected || n.Path == _hovered || (_selected is not null && neighbours.Contains(n.Path));
            if (label && matches) DrawCentered(ctx, n.Path.FileName, it.X, it.Y + it.Radius + 8, 11, AppColors.Text);
        }
    }

    /// <summary>A vertical line through the center of the layout with a "Turn N" mark every few layers.</summary>
    private void PaintTimeAxis(DrawingContext ctx, double w, double h)
    {
        if (_placed.Count == 0 || _graph.Turns < 1) return;
        (Point3 min, Point3 max) = Scene3D.Bounds(_placed.Values);
        double cx = (min.X + max.X) / 2;
        double cy = (min.Y + max.Y) / 2;
        double gap = Scene3D.LayerGap(_positions, _graph.Turns);
        int last = _graph.Turns - 1;
        Projected bottom = _camera.Project(cx, cy, 0, w, h);
        Projected top = _camera.Project(cx, cy, last * gap, w, h);
        using (ctx.PushOpacity(0.5))
        {
            if (bottom.Visible && top.Visible)
                ctx.DrawLine(new Pen(AppColors.Brush(AppColors.Muted), 1, new DashStyle([4, 4], 0)), new Point(bottom.X, bottom.Y), new Point(top.X, top.Y));
            int step = Math.Max(1, (int)Math.Ceiling(_graph.Turns / 8.0));
            for (int t = 0; t <= last; t += step)
            {
                Projected p = _camera.Project(cx, cy, t * gap, w, h);
                if (!p.Visible) continue;
                ctx.DrawEllipse(AppColors.Brush(AppColors.Muted), null, new Point(p.X, p.Y), 2.5, 2.5);
                DrawText(ctx, string.Format(CultureInfo.CurrentCulture, MapStrings.KnowledgeTurnLabelFormat, t + 1), p.X + 6, p.Y - 7, 11, AppColors.Muted);
            }
        }
    }
}
