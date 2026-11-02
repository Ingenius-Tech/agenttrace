using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using GraphRect = AgentTrace.Presentation.Graph.GraphGeometry.Rect;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Draws the heatmap: a treemap of the folders and files of the project, colored by the agent's activity. The
/// layout, scale and texts are pure logic (<c>Presentation.Heatmap</c>); here we only paint (with the layout
/// cached: recomputed only if data, size, zoom, area or filters change) and translate the mouse and keyboard.
/// </summary>
public sealed class HeatmapCanvasView : Control
{
    private const double LegendHeight = 26;
    private const double MinLabelWidth = 36;
    private const double MinLabelHeight = 16;

    private static readonly Typeface Sans = new("Segoe UI, Inter, Helvetica, Arial, sans-serif");
    private static readonly DashStyle Dashed = new([1.5, 1.25], 0);

    private readonly Dictionary<(string Text, double Size, Color Color, int Width), FormattedText> _texts = [];
    private readonly Action<FilePath> _openFile;
    private readonly Action<FilePath> _showDiff;
    private readonly Action<FilePath> _reveal;
    private readonly Action<FilePath> _showInKnowledge;
    private readonly MenuItem _menuOpen = new() { Header = MapStrings.HeatMenuOpen };
    private readonly MenuItem _menuDiff = new() { Header = MapStrings.HeatMenuDiff };
    private readonly MenuItem _menuReveal = new() { Header = MapStrings.HeatMenuReveal };
    private readonly MenuItem _menuKnowledge = new() { Header = MapStrings.HeatMenuKnowledge };
    private readonly MenuItem _menuZoom = new() { Header = MapStrings.HeatMenuZoom };

    private HeatTree? _tree;
    private ActivityFilter _filter = ActivityFilter.All;
    private HeatArea _area = HeatArea.Size;
    private FilePath? _zoom;
    private FilePath? _selected;
    private FilePath? _hovered;
    private HeatNode? _menuTarget;
    private TreemapLayout? _layout;
    private double _layoutWidth;
    private double _layoutHeight;
    private double _maxLeafHeat;
    private int _clicks;

    /// <param name="openFile">Opens a file in the viewer.</param>
    /// <param name="showDiff">Shows its change in the Inspector.</param>
    /// <param name="reveal">Locates it in the project tree.</param>
    /// <param name="showInKnowledge">Selects it in "Knowledge".</param>
    public HeatmapCanvasView(Action<FilePath> openFile, Action<FilePath> showDiff, Action<FilePath> reveal, Action<FilePath> showInKnowledge)
    {
        _openFile = openFile;
        _showDiff = showDiff;
        _reveal = reveal;
        _showInKnowledge = showInKnowledge;
        Focusable = true;
        ClipToBounds = true;
        ContextMenu = BuildContextMenu();

        PointerPressed += (_, e) =>
        {
            Focus();
            _clicks = e.ClickCount;
            Point p = e.GetPosition(this);
            _menuTarget = NodeAt(p.X, p.Y);
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && _menuTarget is { } target) Select(target.Path);
        };
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            Point p = e.GetPosition(this);
            HeatNode? hit = NodeAt(p.X, p.Y);
            Select(hit?.Path);
            if (hit is null || _clicks != 2) return;
            if (hit.Directory) ZoomTo(hit.Path);
            else _openFile(hit.Path);
        };
        PointerMoved += (_, e) =>
        {
            Point p = e.GetPosition(this);
            HeatNode? hit = NodeAt(p.X, p.Y);
            Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
            if (hit?.Path == _hovered) return;
            _hovered = hit?.Path;
            ToolTip.SetTip(this, hit is null || _tree is null ? null : HeatText.Tooltip(hit, _tree));
            InvalidateVisual();
        };
        PointerExited += (_, _) =>
        {
            _hovered = null;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Back || (e.Key == Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
            {
                ZoomUp();
                e.Handled = true;
            }
        };
        SizeChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>User selection (null: none).</summary>
    public event Action<FilePath?>? Selected;

    /// <summary>The folder that fills the view has changed (also when the tree is replaced).</summary>
    public event Action? ZoomChanged;

    public HeatTree? Tree => _tree;

    public ActivityFilter Filter => _filter;

    public HeatArea Area => _area;

    /// <summary>The folder that fills the view (the project root if not zoomed in).</summary>
    public FilePath? Zoom => _zoom ?? _tree?.Root.Path;

    public FilePath? SelectedPath => _selected;

    /// <summary>The current layout (computed if needed); null without a tree.</summary>
    public TreemapLayout? Layout => EnsureLayout();

    /// <summary>Replaces the tree (called often while live): zoom and selection are kept as long as they still exist.</summary>
    public void SetTree(HeatTree? tree)
    {
        _tree = tree;
        if (_zoom is { } z && tree?.Find(z) is not { Directory: true }) _zoom = null;
        _layout = null;
        if (_selected is { } s && tree?.Find(s) is null) Select(null);
        ZoomChanged?.Invoke();
        InvalidateVisual();
    }

    public void SetFilter(ActivityFilter filter)
    {
        _filter = filter;
        _layout = null;
        InvalidateVisual();
    }

    public void SetArea(HeatArea area)
    {
        _area = area;
        _layout = null;
        InvalidateVisual();
    }

    /// <summary>Selects a node (null: clears the selection). What is not in the tree is ignored.</summary>
    public void Select(FilePath? path)
    {
        FilePath? next = path is { } p && _tree?.Find(p) is not null ? p : null;
        if (next == _selected) return;
        _selected = next;
        InvalidateVisual();
        Selected?.Invoke(_selected);
    }

    /// <summary>Selects a file, first zooming the view to a folder containing it if it was not in view.</summary>
    public void Reveal(FilePath path)
    {
        if (_tree is null || _tree.Find(path) is null) return;
        FilePath zoom = Zoom!.Value;
        while (!path.IsUnder(zoom) && zoom != _tree.Root.Path) zoom = zoom.Parent!.Value;
        if (zoom != Zoom) ZoomTo(zoom);
        Select(path);
    }

    /// <summary>Zooms the view into a folder (anything that is not a folder of the tree is ignored).</summary>
    public void ZoomTo(FilePath folder)
    {
        if (_tree?.Find(folder) is not { Directory: true } || folder == Zoom) return;
        _zoom = folder == _tree.Root.Path ? null : folder;
        _layout = null;
        if (_selected is { } s && !s.IsUnder(folder)) Select(null);
        ZoomChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Goes up to the folder containing the current view (nothing if already the root).</summary>
    public void ZoomUp()
    {
        if (_tree is null || Zoom is not { } zoom || zoom == _tree.Root.Path || zoom.Parent is not { } parent) return;
        ZoomTo(parent);
    }

    private TreemapLayout? EnsureLayout()
    {
        if (_tree is null) return null;
        double w = Bounds.Width;
        double h = Math.Max(0, Bounds.Height - LegendHeight);
        if (_layout is not null && w == _layoutWidth && h == _layoutHeight) return _layout;
        HeatNode zoom = (_zoom is { } z ? _tree.Find(z) : null) ?? _tree.Root;
        _layout = TreemapLayout.Of(zoom, w, h, _area, _filter);
        _layoutWidth = w;
        _layoutHeight = h;
        _maxLeafHeat = 0;
        for (int i = 0; i < _layout.Cells.Count; i++)
        {
            if (!HasChildren(_layout.Cells, i)) _maxLeafHeat = Math.Max(_maxLeafHeat, _layout.Cells[i].Node.Activity.Value(_filter));
        }
        return _layout;
    }

    /// <summary>The children of a folder go in the cells after its own, with greater depth.</summary>
    private static bool HasChildren(IReadOnlyList<TreemapCell> cells, int index) =>
        index + 1 < cells.Count && cells[index + 1].Depth > cells[index].Depth;

    private HeatNode? NodeAt(double x, double y) => EnsureLayout()?.HitTest(x, y)?.Node;

    // ---- context menu ----

    private ContextMenu BuildContextMenu()
    {
        _menuOpen.Click += (_, _) => Act(n => _openFile(n.Path));
        _menuDiff.Click += (_, _) => Act(n => _showDiff(n.Path));
        _menuReveal.Click += (_, _) => Act(n => _reveal(n.Path));
        _menuKnowledge.Click += (_, _) => Act(n => _showInKnowledge(n.Path));
        _menuZoom.Click += (_, _) => Act(n => ZoomTo(n.Path));
        var menu = new ContextMenu { Items = { _menuOpen, _menuDiff, _menuReveal, _menuKnowledge, _menuZoom } };
        menu.Opening += (_, e) =>
        {
            if (_menuTarget is not { } node)
            {
                e.Cancel = true;
                return;
            }
            _menuOpen.IsEnabled = !node.Directory;
            _menuDiff.IsEnabled = !node.Directory && node.Kind is not null;
            _menuKnowledge.IsEnabled = !node.Directory && node.Activity.Reads + node.Activity.Writes > 0;
            _menuZoom.IsEnabled = node.Directory && node.Path != Zoom;
        };
        return menu;
    }

    private void Act(Action<HeatNode> action)
    {
        if (_menuTarget is { } node) action(node);
    }

    // ---- painting ----

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        double w = Bounds.Width;
        double h = Bounds.Height;
        ctx.FillRectangle(AppColors.Brush(AppColors.Background), new Rect(0, 0, w, h));
        TreemapLayout? layout = EnsureLayout();
        if (layout is null || layout.Cells.Count == 0)
        {
            string message = _tree is null ? MapStrings.HeatEmptyNoProject
                : _tree.Root.Files == 0 ? MapStrings.HeatEmptyNoFiles
                : MapStrings.HeatEmptyFilter;
            DrawText(ctx, message, 24, 26, 14, AppColors.Muted, 0);
            PaintLegend(ctx, w, h);
            return;
        }
        IReadOnlyList<TreemapCell> cells = layout.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            if (HasChildren(cells, i)) PaintFolder(ctx, cells[i]);
            else PaintLeaf(ctx, cells[i]);
        }
        foreach (TreemapCell cell in cells)
        {
            if (cell.Node.Path == _selected) ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Select), 3), RectOf(cell).Deflate(1.5));
            else if (cell.Node.Path == _hovered) ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Select), 1.5), RectOf(cell).Deflate(0.75));
        }
        PaintLegend(ctx, w, h);
    }

    private static Rect RectOf(TreemapCell cell)
    {
        GraphRect b = cell.Bounds;
        return new Rect(b.X, b.Y, b.W, b.H);
    }

    private void PaintFolder(DrawingContext ctx, TreemapCell cell)
    {
        Rect b = RectOf(cell);
        ctx.DrawRectangle(AppColors.Brush(AppColors.Panel), new Pen(AppColors.Brush(AppColors.BorderSoft), 1), b.Deflate(0.5));
        if (cell.HeaderHeight <= 0) return;
        ctx.FillRectangle(AppColors.Brush(AppColors.Card), new Rect(b.X + 1, b.Y + 1, b.Width - 2, cell.HeaderHeight - 1));
        DrawText(ctx, cell.Node.Name, b.X + 5, b.Y + 2, 11, AppColors.Text, (int)(b.Width - 10));
    }

    private void PaintLeaf(DrawingContext ctx, TreemapCell cell)
    {
        HeatNode node = cell.Node;
        Rect b = RectOf(cell);
        Color fill = Fill(node);
        ctx.FillRectangle(AppColors.Brush(fill), b.Deflate(0.5));

        // Outlines: deleted (dashed red), external-only change (dashed light) and touched in the last turn (solid light)
        if (b.Width >= 8 && b.Height >= 8)
        {
            if (node.Ghost) ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Deleted), 2, Dashed), b.Deflate(1.5));
            else if (node.OnlyExternal) ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Text), 2, Dashed), b.Deflate(1.5));
            if (_tree!.LastTurn >= 0 && node.LastTurn == _tree.LastTurn) ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Text), 1.5), b.Deflate(3.25));
        }

        if (b.Width < MinLabelWidth || b.Height < MinLabelHeight) return;
        string name = node.Kind is { } kind && !node.Directory ? ChangeStyle.Symbol(kind) + " " + node.Name : node.Name;
        // Light or dark label depending on the fill luminance, so it stays readable
        double luminance = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255;
        DrawText(ctx, name, b.X + 4, b.Y + 2, 11, luminance > 0.55 ? AppColors.BadgeText : AppColors.Text, (int)(b.Width - 8));
    }

    private Color Fill(HeatNode node)
    {
        HeatHue hue = HeatScale.Hue(node, _filter);
        if (hue == HeatHue.None) return AppColors.Card;
        Color tone = hue switch
        {
            HeatHue.Created => AppColors.Created,
            HeatHue.Modified => AppColors.Modified,
            HeatHue.Deleted => AppColors.Deleted,
            HeatHue.Read => AppColors.Read,
            HeatHue.Error => AppColors.Error,
            _ => throw new System.Diagnostics.UnreachableException(),
        };
        return AppColors.Lerp(AppColors.Card, tone, HeatScale.Intensity(node.Activity.Value(_filter), _maxLeafHeat));
    }

    /// <summary>The legend, in a strip of its own below the map (it does not cover cells).</summary>
    private void PaintLegend(DrawingContext ctx, double w, double h)
    {
        double top = h - LegendHeight;
        ctx.FillRectangle(AppColors.Brush(AppColors.Panel), new Rect(0, top, w, LegendHeight));
        double y = top + LegendHeight / 2;
        string[] labels = [MapStrings.LegendCreated, MapStrings.LegendModified, MapStrings.LegendDeleted, MapStrings.LegendRead, MapStrings.LegendError];
        Color[] colors = [AppColors.Created, AppColors.Modified, AppColors.Deleted, AppColors.Read, AppColors.Error];
        double x = 14;
        for (int i = 0; i < labels.Length; i++)
        {
            ctx.DrawRectangle(AppColors.Brush(colors[i]), null, new Rect(x, y - 5, 10, 10));
            DrawText(ctx, labels[i], x + 15, y - 8, 11, AppColors.Muted, 0);
            x += 25 + labels[i].Length * 6.2;
        }
        DrawText(ctx, MapStrings.HeatLegendNote, x + 10, y - 8, 11, AppColors.Muted, 0);
    }

    /// <summary>Draws the text on one line; with <paramref name="maxWidth"/> greater than 0 it is truncated with an ellipsis.</summary>
    private void DrawText(DrawingContext ctx, string text, double x, double y, double size, Color color, int maxWidth)
    {
        if (_texts.Count > 3000) _texts.Clear();
        var key = (text, size, color, maxWidth);
        if (!_texts.TryGetValue(key, out FormattedText? formatted))
        {
            formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Sans, size, AppColors.Brush(color));
            if (maxWidth > 0)
            {
                formatted.MaxTextWidth = maxWidth;
                formatted.MaxLineCount = 1;
                formatted.Trimming = TextTrimming.CharacterEllipsis;
            }
            _texts[key] = formatted;
        }
        ctx.DrawText(formatted, new Point(x, y));
    }
}
