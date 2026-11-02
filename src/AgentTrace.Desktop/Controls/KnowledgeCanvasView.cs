using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Draws the knowledge graph of files: one node per file (with the color code of the tree: created, modified, deleted,
/// read), solid lines between those that change together and dashed arrows from what was read to what was edited. The layout,
/// zoom and filters are pure logic (<c>Presentation.Knowledge</c>); here we only paint and translate the wheel, dragging
/// and clicks.
/// </summary>
public sealed partial class KnowledgeCanvasView : Control
{
    private const double Dim = 0.22;

    private static readonly Typeface Sans = new("Segoe UI, Inter, Helvetica, Arial, sans-serif");

    private readonly PanZoom _view = new();
    private readonly Dictionary<(string Text, double Size, Color Color), FormattedText> _texts = [];

    private KnowledgeGraph _graph = new KnowledgeBuilder().Snapshot();
    private KnowledgeView _shown;
    private Dictionary<FilePath, ForceLayout.Point> _positions = [];
    private FilePath? _selected;
    private FilePath? _hovered;
    private FilePath? _projectRoot;
    private string _query = "";
    private bool _onlyChanged;
    private int _minWeight = 1;
    private bool _fitted;
    private bool _dragged;
    private Point _dragFrom;
    private int _clicks;

    public KnowledgeCanvasView()
    {
        _shown = KnowledgeView.Of(_graph, false, KnowledgeView.MaxNodes);
        Focusable = true;
        ClipToBounds = true;
        PointerWheelChanged += (_, e) =>
        {
            Point p = e.GetPosition(this);
            if (_mode3D)
            {
                _camera.Zoom(e.Delta.Y > 0 ? 1.12 : 1 / 1.12);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            _view.ZoomAt(e.Delta.Y > 0 ? 1.12 : 1 / 1.12, p.X, p.Y);
            InvalidateVisual();
            e.Handled = true;
        };
        PointerPressed += (_, e) =>
        {
            Focus();
            _dragFrom = e.GetPosition(this);
            _dragged = false;
            _clicks = e.ClickCount;
        };
        PointerMoved += OnMoved;
        PointerReleased += (_, e) =>
        {
            if (_dragged || e.InitialPressMouseButton != MouseButton.Left) return;
            Point p = e.GetPosition(this);
            FilePath? hit = NodeAt(p.X, p.Y);
            Select(hit);
            if (hit is { } path && _clicks == 2) Activated?.Invoke(path);
        };
        PointerExited += (_, _) =>
        {
            _hovered = null;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        };
        SizeChanged += (_, _) =>
        {
            if (_mode3D) FitOnce3D();
            FitOnce();
            InvalidateVisual();
        };
    }

    public event Action<FilePath?>? Selected;

    /// <summary>Double click on a file.</summary>
    public event Action<FilePath>? Activated;

    public KnowledgeGraph Graph => _graph;

    public KnowledgeView Shown => _shown;

    public FilePath? SelectedPath => _selected;

    public PanZoom View => _view;

    /// <summary>Files the graph can draw (nodes with only errors belong to the heatmap and do not count).</summary>
    public int DrawableNodes => _shown.Nodes.Count + _shown.Hidden;

    public bool Has(FilePath path) => _positions.ContainsKey(path);

    public void SetProjectRoot(FilePath? root) => _projectRoot = root;

    /// <summary>Replaces the graph (called often while live): the layout starts from the previous positions.</summary>
    public void SetGraph(KnowledgeGraph graph)
    {
        _graph = graph;
        Relayout();
    }

    public void SetOnlyChanged(bool value)
    {
        _onlyChanged = value;
        Relayout();
    }

    /// <summary><c>true</c>: only the relations that repeat in more than one turn.</summary>
    public void SetRepeatedOnly(bool value)
    {
        _minWeight = value ? 2 : 1;
        Relayout();
    }

    /// <summary>Dims the files whose name does not contain the text.</summary>
    public void SetQuery(string? text)
    {
        _query = (text ?? "").Trim().ToLowerInvariant();
        InvalidateVisual();
    }

    /// <summary>Selects a file and centers it. Null clears the selection. A file that is not visible is ignored.</summary>
    public void Select(FilePath? path)
    {
        _selected = path is { } p && _positions.ContainsKey(p) ? p : null;
        if (_selected is { } s)
        {
            ForceLayout.Point pt = _positions[s];
            if (_mode3D && _placed.TryGetValue(s, out Point3? q)) _camera.SetTarget(q.X, q.Y, q.Z);
            else _view.CenterOn(pt.X, pt.Y, Bounds.Width, Bounds.Height);
        }
        InvalidateVisual();
        Selected?.Invoke(_selected);
    }

    /// <summary>Fits the whole graph in the window.</summary>
    public void Fit()
    {
        if (_positions.Count == 0) return;
        if (_mode3D)
        {
            Fit3D();
            return;
        }
        double minX = _positions.Values.Min(p => p.X);
        double minY = _positions.Values.Min(p => p.Y);
        double maxX = _positions.Values.Max(p => p.X);
        double maxY = _positions.Values.Max(p => p.Y);
        _view.Fit(minX, minY, maxX, maxY, Bounds.Width, Bounds.Height, 50);
        InvalidateVisual();
    }

    public void SetZoom(double scale)
    {
        _view.SetScale(scale, Bounds.Width / 2, Bounds.Height / 2);
        InvalidateVisual();
    }

    private void Relayout()
    {
        _shown = KnowledgeView.Of(_graph, _onlyChanged, _minWeight, KnowledgeView.MaxNodes);
        bool cold = _positions.Count == 0;
        _positions = ForceLayout.Run(_shown, _positions, cold ? 150 : 25);
        _placed = Scene3D.Place(_shown, _positions, _graph.Turns);
        if (_selected is { } s && !_positions.ContainsKey(s))
        {
            _selected = null;
            Selected?.Invoke(null);
        }
        FitOnce();
        InvalidateVisual();
    }

    private void FitOnce()
    {
        if (_fitted || _positions.Count == 0 || Bounds.Width <= 0) return;
        Fit();
        _fitted = true;
    }

    // ---- interaction ----

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        Point p = e.GetPosition(this);
        if (_mode3D && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(p.X - _dragFrom.X) + Math.Abs(p.Y - _dragFrom.Y) > 3) _dragged = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _camera.Pan(p.X - _dragFrom.X, p.Y - _dragFrom.Y, Bounds.Height);
            else _camera.Orbit((p.X - _dragFrom.X) * 0.008, (p.Y - _dragFrom.Y) * 0.008);
            _dragFrom = p;
            InvalidateVisual();
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(p.X - _dragFrom.X) + Math.Abs(p.Y - _dragFrom.Y) > 3) _dragged = true;
            _view.PanBy(p.X - _dragFrom.X, p.Y - _dragFrom.Y);
            _dragFrom = p;
            InvalidateVisual();
            return;
        }
        FilePath? hit = NodeAt(p.X, p.Y);
        Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (hit is null)
        {
            if (_hovered is not null)
            {
                _hovered = null;
                ToolTip.SetTip(this, null);
                InvalidateVisual();
            }
        }
        else if (hit != _hovered)
        {
            _hovered = hit;
            ToolTip.SetTip(this, TipText(hit.Value));
            InvalidateVisual();
        }
    }

    private FilePath? NodeAt(double sx, double sy)
    {
        if (_mode3D) return Scene3D.HitTest(_items, sx, sy)?.Path;
        double wx = _view.ToWorldX(sx);
        double wy = _view.ToWorldY(sy);
        FilePath? best = null;
        double bestDist = double.MaxValue;
        foreach (Node n in _shown.Nodes)
        {
            if (!_positions.TryGetValue(n.Path, out ForceLayout.Point? p)) continue;
            double d = Math.Sqrt((p.X - wx) * (p.X - wx) + (p.Y - wy) * (p.Y - wy));
            if (d <= KnowledgeView.Radius(n) + 3 / _view.Scale && d < bestDist)
            {
                best = n.Path;
                bestDist = d;
            }
        }
        return best;
    }

    private string TipText(FilePath path)
    {
        Node? n = _graph.FindNode(path);
        if (n is null) return path.ToString();
        string relative = NodeDetail.RelativeTo(path, _projectRoot);
        return n.External > 0
            ? string.Format(CultureInfo.CurrentCulture, MapStrings.KnowledgeTipExternalFormat, relative, n.Reads, n.AgentChanges, n.External)
            : string.Format(CultureInfo.CurrentCulture, MapStrings.KnowledgeTipFormat, relative, n.Reads, n.AgentChanges);
    }

    // ---- painting ----

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        double w = Bounds.Width;
        double h = Bounds.Height;
        ctx.FillRectangle(AppColors.Brush(AppColors.Background), new Rect(0, 0, w, h));
        if (_shown.Nodes.Count == 0)
        {
            DrawText(ctx, DrawableNodes == 0 ? MapStrings.KnowledgeEmptyNoFiles : MapStrings.KnowledgeEmptyFilter, 24, 26, 14, AppColors.Muted);
            return;
        }
        HashSet<FilePath> neighbours = Neighbours();
        if (_mode3D)
        {
            Render3D(ctx, neighbours);
            PaintOverlay(ctx, w, h);
            return;
        }
        using (ctx.PushTransform(Matrix.CreateScale(_view.Scale, _view.Scale) * Matrix.CreateTranslation(_view.TranslateX, _view.TranslateY)))
        {
            PaintEdges(ctx, neighbours);
            foreach (Node n in _shown.Nodes) PaintNode(ctx, n, neighbours);
        }
        PaintOverlay(ctx, w, h);
    }

    /// <summary>The selected file and those related to it (empty if there is no selection).</summary>
    private HashSet<FilePath> Neighbours()
    {
        HashSet<FilePath> output = [];
        if (_selected is not { } s) return output;
        output.Add(s);
        foreach (Edge e in _shown.Edges)
        {
            if (e.From == s) output.Add(e.To);
            if (e.To == s) output.Add(e.From);
        }
        return output;
    }

    private void PaintEdges(DrawingContext ctx, HashSet<FilePath> neighbours)
    {
        double scale = _view.Scale;
        double inv = Math.Max(scale, 0.5);
        // With many relations, each line is lightened so groups show up instead of a blur
        double crowded = _shown.Edges.Count > 800 ? 0.2 : _shown.Edges.Count > 250 ? 0.45 : 0.8;
        foreach (Edge e in _shown.Edges)
        {
            if (!_positions.TryGetValue(e.From, out ForceLayout.Point? a) || !_positions.TryGetValue(e.To, out ForceLayout.Point? b)) continue;
            bool related = _selected is { } s && (e.From == s || e.To == s);
            double alpha = _selected is null ? crowded : related ? 0.9 : 0.08;
            double width = (1 + Math.Log(e.Weight)) * (related ? 1.6 : 1) / inv;
            using (ctx.PushOpacity(alpha))
            {
                if (e.Relation == Relation.CoChanged)
                {
                    var pen = new Pen(AppColors.Brush(related ? AppColors.Text : AppColors.Border), width, null, PenLineCap.Round);
                    ctx.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
                }
                else
                {
                    var pen = new Pen(AppColors.Brush(AppColors.Read), width, new DashStyle([6 / inv / width, 4 / inv / width], 0), PenLineCap.Round);
                    ctx.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
                    ArrowHead(ctx, a, b, NodeRadius(e.To), 7 / Math.Max(scale, 0.6));
                }
            }
        }
    }

    private double NodeRadius(FilePath path) => _graph.FindNode(path) is { } n ? KnowledgeView.Radius(n) : 8.0;

    private static void ArrowHead(DrawingContext ctx, ForceLayout.Point from, ForceLayout.Point to, double targetRadius, double size)
    {
        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        double tipX = to.X - Math.Cos(angle) * (targetRadius + 2);
        double tipY = to.Y - Math.Sin(angle) * (targetRadius + 2);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(new Point(tipX, tipY), true);
            g.LineTo(new Point(tipX - Math.Cos(angle - 0.45) * size, tipY - Math.Sin(angle - 0.45) * size));
            g.LineTo(new Point(tipX - Math.Cos(angle + 0.45) * size, tipY - Math.Sin(angle + 0.45) * size));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(AppColors.Brush(AppColors.Read), null, geometry);
    }

    private void PaintNode(DrawingContext ctx, Node n, HashSet<FilePath> neighbours)
    {
        if (!_positions.TryGetValue(n.Path, out ForceLayout.Point? p)) return;
        double r = KnowledgeView.Radius(n);
        bool matches = _query.Length == 0 || n.Path.FileName.ToLowerInvariant().Contains(_query, StringComparison.Ordinal);
        bool inFocus = _selected is null || neighbours.Contains(n.Path);
        using (ctx.PushOpacity(matches && inFocus ? 1 : matches ? 0.4 : Dim))
        {
            var center = new Point(p.X, p.Y);
            Pen outline = n.OnlyExternal
                ? new Pen(AppColors.Brush(AppColors.Text), 2, new DashStyle([1.5, 1.25], 0))
                : new Pen(AppColors.Brush(AppColors.Background), 1.5);
            ctx.DrawEllipse(AppColors.Brush(AppColors.Status(n.LastChange)), outline, center, r, r);
            // The state does not depend on color alone: + created, ~ modified, − deleted, an eye if only read
            if (n.LastChange is not { } kind) DrawIcon(ctx, Codicon.Eye, p.X, p.Y, r * 1.2, AppColors.BadgeText);
            else DrawCentered(ctx, ChangeStyle.Symbol(kind), p.X, p.Y, r * 1.3, AppColors.BadgeText);
            if (n.Path == _selected || n.Path == _hovered)
            {
                var pen = new Pen(AppColors.Brush(AppColors.Select), n.Path == _selected ? 3 : 1.5);
                ctx.DrawEllipse(null, pen, center, r + 3, r + 3);
            }
            bool label = _view.Scale >= 0.8 || r >= 13 || n.Path == _selected || n.Path == _hovered
                         || (_selected is not null && neighbours.Contains(n.Path));
            if (label && matches) DrawCentered(ctx, n.Path.FileName, p.X, p.Y + r + 13 - 6, 11, AppColors.Text);
        }
    }

    /// <summary>Legend and count, in screen coordinates.</summary>
    private void PaintOverlay(DrawingContext ctx, double w, double h)
    {
        if (_shown.Hidden > 0) DrawText(ctx, string.Format(CultureInfo.CurrentCulture, MapStrings.KnowledgeShowingFormat, _shown.Nodes.Count, DrawableNodes), 14, 8, 11, AppColors.Muted);
        double y = h - 14;
        string[] labels = [MapStrings.LegendCreated, MapStrings.LegendModified, MapStrings.LegendDeleted, MapStrings.LegendRead];
        Color[] colors = [AppColors.Created, AppColors.Modified, AppColors.Deleted, AppColors.Read];
        double x = 14;
        for (int i = 0; i < labels.Length; i++)
        {
            ctx.DrawEllipse(AppColors.Brush(colors[i]), null, new Point(x + 5, y - 4), 5, 5);
            DrawText(ctx, labels[i], x + 15, y - 11, 11, AppColors.Muted);
            x += 25 + labels[i].Length * 6.2;
        }
        DrawText(ctx, MapStrings.KnowledgeLegendNote, x + 10, y - 11, 11, AppColors.Muted);
    }

    private FormattedText Text(string text, double size, Color color, Typeface typeface)
    {
        if (_texts.Count > 3000) _texts.Clear();
        var key = (text + "\u0001" + typeface.FontFamily.Name, size, color);
        if (!_texts.TryGetValue(key, out FormattedText? formatted))
        {
            formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, AppColors.Brush(color));
            _texts[key] = formatted;
        }
        return formatted;
    }

    private void DrawText(DrawingContext ctx, string text, double x, double y, double size, Color color) =>
        ctx.DrawText(Text(text, size, color, Sans), new Point(x, y));

    private void DrawCentered(DrawingContext ctx, string text, double cx, double cy, double size, Color color)
    {
        FormattedText t = Text(text, size, color, Sans);
        ctx.DrawText(t, new Point(cx - t.Width / 2, cy - t.Height / 2));
    }

    private void DrawIcon(DrawingContext ctx, Codicon icon, double cx, double cy, double size, Color color)
    {
        FormattedText t = Text(Codicons.Glyph(icon), size, color, new Typeface(Codicons.Font));
        ctx.DrawText(t, new Point(cx - t.Width / 2, cy - t.Height / 2));
    }
}
