using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Panorama;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Draws the Areas map: the rows (areas) on the left, the tasks and turns on top and, in between, one cell per area and turn colored by
/// what the agent did. The matrix and the geometry are pure logic (<c>Presentation.Areas</c>); here we only paint and translate the mouse
/// and keyboard. Only the visible rows and columns are drawn.
/// </summary>
public sealed class AreasCanvasView : Control
{
    private static readonly Typeface Sans = new("Segoe UI, Inter, Helvetica, Arial, sans-serif");
    private static readonly Typeface SansBold = new("Segoe UI, Inter, Helvetica, Arial, sans-serif", FontStyle.Normal, FontWeight.Bold);

    private readonly Dictionary<(string Text, double Size, Color Color, int Width, bool Bold), FormattedText> _texts = [];
    private readonly Dictionary<(Codicon Icon, Color Color), FormattedText> _glyphs = [];
    private readonly HashSet<AreaRowKey> _expanded = [];
    private readonly Action<FilePath> _openFile;

    private AreasMatrix _matrix = AreasMatrix.Empty;
    private IReadOnlyList<AreaDisplayRow> _lines = [];
    private double _scrollX;
    private double _scrollY;
    private double _zoom = 1;
    private AreaRowKey? _selectedRow;
    private int? _selectedTurn;
    private int? _selectedTask;
    private FocusTargets? _focus;
    private AreasHit _hovered = AreasHit.None;
    private int _clicks;

    /// <param name="openFile">Opens a file in the viewer (double click on the row of a file).</param>
    public AreasCanvasView(Action<FilePath> openFile)
    {
        _openFile = openFile;
        Focusable = true;
        ClipToBounds = true;

        PointerPressed += (_, e) =>
        {
            Focus();
            _clicks = e.ClickCount;
        };
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            Point p = e.GetPosition(this);
            OnClick(HitAt(p.X, p.Y), _clicks);
        };
        PointerMoved += (_, e) =>
        {
            Point p = e.GetPosition(this);
            AreasHit hit = HitAt(p.X, p.Y);
            if (hit == _hovered) return;
            _hovered = hit;
            Cursor = hit.Kind == AreasHitKind.None ? Cursor.Default : new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, TooltipOf(hit));
            InvalidateVisual();
        };
        PointerExited += (_, _) =>
        {
            _hovered = AreasHit.None;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        };
        PointerWheelChanged += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) SetZoom(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0) Scroll(-(e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 80, 0);
            else Scroll(0, -e.Delta.Y * 60);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Select(null);
            SelectTask(null);
            e.Handled = true;
        };
        SizeChanged += (_, _) =>
        {
            ClampScroll();
            InvalidateVisual();
        };
    }

    /// <summary>The selected cell changed (also when it disappears with a new matrix).</summary>
    public event Action? CellSelectionChanged;

    /// <summary>The selected task changed.</summary>
    public event Action? TaskSelectionChanged;

    /// <summary>A turn header was clicked by the user (a selection the Panorama links to the other panes).</summary>
    public event Action<int>? TurnHeaderClicked;

    public AreasMatrix Matrix => _matrix;

    /// <summary>The focus the Panorama is showing (columns and rows it highlights), or null.</summary>
    public FocusTargets? LinkedFocus => _focus;

    public double ScrollX => _scrollX;

    public double ScrollY => _scrollY;

    /// <summary>What the canvas says in place of the map while there is nothing to draw; null when it draws the map.</summary>
    public string? EmptyMessage => !_matrix.IsEmpty ? null
        : _matrix.Snapshot.Project == AreasSnapshot.Empty.Project ? AreaStrings.EmptyNoProject
        : _matrix.Snapshot.Activity.Turns.Count == 0 ? AreaText.EmptyNoActivity(_matrix.Snapshot)
        : AreaStrings.EmptyFilter;

    /// <summary>The lines on the screen (area rows and, if expanded, their files).</summary>
    public IReadOnlyList<AreaDisplayRow> Lines => _lines;

    public double Zoom => _zoom;

    public int? SelectedTask => _selectedTask;

    public int? SelectedTurn => _selectedTurn;

    public AreaRowKey? SelectedRowKey => _selectedRow;

    /// <summary>Row (index into <see cref="AreasMatrix.Rows"/>) of the selection, or -1.</summary>
    public int SelectedRow => _selectedRow is { } key ? _matrix.Rows.ToList().FindIndex(r => r.Key == key) : -1;

    /// <summary>The selected cell as (row, column), or null (it needs a row and a turn that are visible).</summary>
    public (int Row, int Column)? SelectedCell
    {
        get
        {
            int row = SelectedRow;
            int column = _selectedTurn is { } t ? _matrix.ColumnOfTurn(t) : -1;
            return row >= 0 && column >= 0 ? (row, column) : null;
        }
    }

    public bool IsExpanded(AreaRowKey key) => _expanded.Contains(key);

    /// <summary>Replaces the matrix (called often while live): expansion, selection and scroll are kept as long as they still exist.</summary>
    public void SetMatrix(AreasMatrix matrix)
    {
        (int Row, int Column)? before = SelectedCell;
        _matrix = matrix;
        _expanded.IntersectWith(matrix.Rows.Select(r => r.Key));
        if (_selectedRow is { } key && !matrix.Rows.Any(r => r.Key == key)) _selectedRow = null;
        if (_selectedTurn is { } turn && matrix.ColumnOfTurn(turn) < 0) _selectedTurn = null;
        if (_selectedTask is { } task && task >= matrix.Snapshot.Tasks.Count) _selectedTask = null;
        RebuildLines();
        if (SelectedCell != before) CellSelectionChanged?.Invoke();
    }

    /// <summary>Selects the cell of an area row (index into <see cref="AreasMatrix.Rows"/>) and a column; null clears it.</summary>
    public void Select(int? row, int column = -1)
    {
        AreaRowKey? key = row is { } r && r >= 0 && r < _matrix.Rows.Count ? _matrix.Rows[r].Key : null;
        int? turn = key is not null && column >= 0 && column < _matrix.Columns.Count ? _matrix.Columns[column].Turn : null;
        if (key == _selectedRow && turn == _selectedTurn) return;
        _selectedRow = key;
        _selectedTurn = turn;
        InvalidateVisual();
        CellSelectionChanged?.Invoke();
    }

    /// <summary>Selects a task (the others are dimmed and its propagation is drawn); null clears it.</summary>
    public void SelectTask(int? task)
    {
        int? next = task is { } t && t >= 0 && t < _matrix.Snapshot.Tasks.Count ? t : null;
        if (next == _selectedTask) return;
        _selectedTask = next;
        InvalidateVisual();
        TaskSelectionChanged?.Invoke();
    }

    /// <summary>Highlights a turn column without opening a panel.</summary>
    public void SelectTurn(int turn)
    {
        Select(null);
        _selectedTurn = _matrix.ColumnOfTurn(turn) >= 0 ? turn : null;
        InvalidateVisual();
    }

    /// <summary>
    /// Highlights the turn columns and the area rows of a focus and scrolls the first of each into view. It is not a selection: it raises no
    /// event and does not open the cell panel. Null or empty targets clear it.
    /// </summary>
    public void ShowFocus(FocusTargets? targets, bool reveal = true)
    {
        _focus = targets is { IsEmpty: false } ? targets : null;
        if (_focus is { } focus && reveal)
        {
            if (focus.FirstTurn is { } turn) RevealColumn(turn);
            int line = _lines.ToList().FindIndex(l => l.File is null && !l.IsMore && focus.AreaRows.Contains(_matrix.Rows[l.Row].Key));
            if (line >= 0) RevealLine(line);
        }
        InvalidateVisual();
    }

    /// <summary>Scrolls horizontally so the column of a turn is visible (centered when it was not). Nothing if the turn has no column.</summary>
    public void RevealColumn(int turn)
    {
        int column = _matrix.ColumnOfTurn(turn);
        if (column < 0) return;
        double width = AreasGeometry.ColumnWidth * _zoom;
        if (column * width < _scrollX || (column + 1) * width > _scrollX + ViewportWidth) _scrollX = column * width + width / 2 - ViewportWidth / 2;
        ClampScroll();
        InvalidateVisual();
    }

    private void RevealLine(int line)
    {
        double top = line * AreasGeometry.RowHeight;
        if (top < _scrollY || top + AreasGeometry.RowHeight > _scrollY + ViewportHeight) _scrollY = top + AreasGeometry.RowHeight / 2 - ViewportHeight / 2;
        ClampScroll();
    }

    private static IBrush FocusBrush(double alpha) => new SolidColorBrush(AppColors.Select, alpha);

    private bool ColumnFocused(int column) => _focus is { } focus && focus.HasTurn(_matrix.Columns[column].Turn);

    private bool LineFocused(AreaDisplayRow line) => _focus is { } focus && !line.IsMore && focus.AreaRows.Contains(_matrix.Rows[line.Row].Key);

    public void ToggleExpanded(AreaRowKey key)
    {
        if (!_expanded.Remove(key)) _expanded.Add(key);
        RebuildLines();
    }

    public void Expand(AreaRowKey key)
    {
        if (_expanded.Add(key)) RebuildLines();
    }

    private void RebuildLines()
    {
        _lines = _matrix.DisplayRows(_expanded);
        ClampScroll();
        InvalidateVisual();
    }

    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, AreasGeometry.MinZoom, AreasGeometry.MaxZoom);
        ClampScroll();
        InvalidateVisual();
    }

    private double ViewportWidth => Math.Max(0, Bounds.Width - AreasGeometry.HeaderWidth);

    private double ViewportHeight => Math.Max(0, Bounds.Height - AreasGeometry.TopHeight - LegendHeight);

    private const double LegendHeight = 26;

    private void Scroll(double dx, double dy)
    {
        _scrollX += dx;
        _scrollY += dy;
        ClampScroll();
        InvalidateVisual();
    }

    private void ClampScroll()
    {
        (double width, double height) = AreasGeometry.ContentSize(_lines.Count, _matrix.Columns.Count, _zoom);
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, width - ViewportWidth));
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, height - ViewportHeight));
    }

    // ---- hit testing and interaction ----

    /// <summary>What is at a point of the control.</summary>
    public AreasHit HitAt(double x, double y) =>
        y > Bounds.Height - LegendHeight ? AreasHit.None
            : AreasGeometry.HitTest(x, y, _scrollX, _scrollY, _zoom, _lines.Count, _matrix.Columns.Count, _matrix.Bands);

    /// <summary>The center of a cell (line and column), in coordinates of this control.</summary>
    public Point CenterOf(int line, int column) => new(
        AreasGeometry.ColumnX(column, _scrollX, _zoom) + AreasGeometry.ColumnWidth * _zoom / 2,
        AreasGeometry.RowY(line, _scrollY) + AreasGeometry.RowHeight / 2);

    public Point CenterOfRowHeader(int line) => new(AreasGeometry.HeaderWidth / 2, AreasGeometry.RowY(line, _scrollY) + AreasGeometry.RowHeight / 2);

    public Point CenterOfTaskHeader(int band) => new(
        AreasGeometry.ColumnX(_matrix.Bands[band].FirstColumn, _scrollX, _zoom) + 8, AreasGeometry.TaskBandHeight / 2);

    private void OnClick(AreasHit hit, int clicks)
    {
        switch (hit.Kind)
        {
            case AreasHitKind.Cell:
                Select(_lines[hit.Row].Row, hit.Column);
                break;
            case AreasHitKind.RowHeader:
                AreaDisplayRow line = _lines[hit.Row];
                if (line.IsMore) break;
                if (line.File is { } file)
                {
                    if (clicks == 2) _openFile(file);
                    break;
                }
                Select(line.Row, -1);
                if (clicks == 2) ToggleExpanded(_matrix.Rows[line.Row].Key);
                break;
            case AreasHitKind.TaskHeader:
                SelectTask(_matrix.Bands[hit.Column].Task.Index == _selectedTask ? null : _matrix.Bands[hit.Column].Task.Index);
                break;
            case AreasHitKind.TurnHeader:
                SelectTurn(_matrix.Columns[hit.Column].Turn);
                TurnHeaderClicked?.Invoke(_matrix.Columns[hit.Column].Turn);
                break;
            default:
                Select(null);
                break;
        }
    }

    private string? TooltipOf(AreasHit hit)
    {
        switch (hit.Kind)
        {
            case AreasHitKind.Cell:
                AreaDisplayRow line = _lines[hit.Row];
                FileActivity a = _matrix.ActivityAt(line, hit.Column);
                if (a.Total == 0 || line.IsMore) return null;
                return AreaText.CellTitle(line.Label, _matrix.Columns[hit.Column].Turn) + "\n" + AreaText.Counts(a, _matrix.Filter);
            case AreasHitKind.RowHeader:
                AreaDisplayRow row = _lines[hit.Row];
                return row.IsMore ? null : row.File is null
                    ? AreaText.RowTooltip(row.Label, row.Total, _matrix.Rows[row.Row].Files, _matrix.Filter)
                    : row.Label + "\n" + AreaText.Counts(row.Total, _matrix.Filter);
            case AreasHitKind.TaskHeader:
                AreaTask task = _matrix.Bands[hit.Column].Task;
                string? prompt = _matrix.Snapshot.Flow.Turns[task.FirstTurn].Prompt;
                AreaTaskSummary summary = _matrix.Snapshot.Summaries[task.Index];
                List<string> lines = [AreaText.TaskTitle(task)];
                if (!string.IsNullOrWhiteSpace(prompt)) lines.Add(Shorten(prompt));
                lines.Add(AreaText.TaskCounts(summary));
                if (AreaText.TaskOrder(summary, _matrix.Snapshot.NameOf) is { } order) lines.Add(order);
                lines.AddRange(AreaText.BadgeTexts(summary));
                return string.Join("\n", lines);
            case AreasHitKind.TurnHeader:
                return AreaText.TurnTitle(_matrix.Columns[hit.Column].Turn);
            default:
                return null;
        }
    }

    private static string Shorten(string text)
    {
        string flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 240 ? flat : flat[..240] + "…";
    }

    // ---- painting ----

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        double w = Bounds.Width;
        double h = Bounds.Height;
        ctx.FillRectangle(AppColors.Brush(AppColors.Background), new Rect(0, 0, w, h));
        if (_matrix.IsEmpty)
        {
            DrawText(ctx, EmptyMessage!, 24, 26, 14, AppColors.Muted, 0);
            PaintLegend(ctx, w, h);
            return;
        }
        PaintCells(ctx, w, h);
        PaintPropagation(ctx, w, h);
        PaintTopHeader(ctx, w);
        PaintRowHeaders(ctx, h);
        ctx.FillRectangle(AppColors.Brush(AppColors.Panel), new Rect(0, 0, AreasGeometry.HeaderWidth, AreasGeometry.TopHeight));
        ctx.DrawLine(new Pen(AppColors.Brush(AppColors.BorderSoft), 1), new Point(0, AreasGeometry.TopHeight), new Point(w, AreasGeometry.TopHeight));
        PaintLegend(ctx, w, h);
    }

    private (int First, int Last) VisibleColumns()
    {
        double width = AreasGeometry.ColumnWidth * _zoom;
        int first = Math.Max(0, (int)(_scrollX / width));
        int last = Math.Min(_matrix.Columns.Count - 1, (int)((_scrollX + ViewportWidth) / width));
        return (first, last);
    }

    private (int First, int Last) VisibleLines()
    {
        int first = Math.Max(0, (int)(_scrollY / AreasGeometry.RowHeight));
        int last = Math.Min(_lines.Count - 1, (int)((_scrollY + ViewportHeight) / AreasGeometry.RowHeight));
        return (first, last);
    }

    private bool Dimmed(int column) =>
        _selectedTask is { } t && _matrix.Bands[_matrix.Columns[column].Band].Task.Index != t;

    private void PaintCells(DrawingContext ctx, double w, double h)
    {
        (int c0, int c1) = VisibleColumns();
        (int l0, int l1) = VisibleLines();
        double cw = AreasGeometry.ColumnWidth * _zoom;
        using DrawingContext.PushedState clip = ctx.PushClip(new Rect(AreasGeometry.HeaderWidth, AreasGeometry.TopHeight, Math.Max(0, w - AreasGeometry.HeaderWidth), Math.Max(0, h - AreasGeometry.TopHeight - LegendHeight)));
        if (_focus is not null)
        {
            for (int c = c0; c <= c1; c++)
            {
                if (ColumnFocused(c)) ctx.FillRectangle(FocusBrush(0.16), new Rect(AreasGeometry.ColumnX(c, _scrollX, _zoom), AreasGeometry.TopHeight, cw, h));
            }
        }
        for (int l = l0; l <= l1; l++)
        {
            AreaDisplayRow line = _lines[l];
            double y = AreasGeometry.RowY(l, _scrollY);
            if (l % 2 == 1) ctx.FillRectangle(AppColors.Brush(AppColors.Panel), new Rect(AreasGeometry.HeaderWidth, y, w, AreasGeometry.RowHeight));
            if (LineFocused(line)) ctx.FillRectangle(FocusBrush(0.12), new Rect(AreasGeometry.HeaderWidth, y, w, AreasGeometry.RowHeight));
            if (line.IsMore) continue;
            for (int c = c0; c <= c1; c++)
            {
                FileActivity a = _matrix.ActivityAt(line, c);
                HeatHue hue = AreasMatrix.Hue(a, _matrix.Filter);
                if (hue == HeatHue.None) continue;
                Color tone = hue switch { HeatHue.Error => AppColors.Error, HeatHue.Modified => AppColors.Modified, _ => AppColors.Read };
                Color fill = AppColors.Lerp(AppColors.Card, tone, HeatScale.Intensity(a.Value(_matrix.Filter), _matrix.MaxCellValue));
                double x = AreasGeometry.ColumnX(c, _scrollX, _zoom);
                var rect = new Rect(x + 1, y + 1, cw - 2, AreasGeometry.RowHeight - 2);
                using DrawingContext.PushedState opacity = Dimmed(c) ? ctx.PushOpacity(0.25) : default;
                ctx.FillRectangle(AppColors.Brush(fill), rect);
                if (cw >= 28)
                {
                    double luminance = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255;
                    DrawText(ctx, a.Value(_matrix.Filter).ToString(CultureInfo.CurrentCulture), x + 4, y + 5, 11, luminance > 0.55 ? AppColors.BadgeText : AppColors.Text, (int)(cw - 8));
                }
            }
        }
        if (SelectedCell is { } selected && _lines.Select((x, i) => (x, i)).FirstOrDefault(p => p.x.Row == selected.Row && p.x.File is null) is { x: not null } at)
        {
            double x = AreasGeometry.ColumnX(selected.Column, _scrollX, _zoom);
            ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Select), 2), new Rect(x + 1, AreasGeometry.RowY(at.i, _scrollY) + 1, cw - 2, AreasGeometry.RowHeight - 2));
        }
        if (_hovered.Kind == AreasHitKind.Cell)
        {
            double x = AreasGeometry.ColumnX(_hovered.Column, _scrollX, _zoom);
            ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Select), 1), new Rect(x + 0.5, AreasGeometry.RowY(_hovered.Row, _scrollY) + 0.5, cw - 1, AreasGeometry.RowHeight - 1));
        }
    }

    /// <summary>With a task selected: a line through the first change of each area, in time order.</summary>
    private void PaintPropagation(DrawingContext ctx, double w, double h)
    {
        if (_selectedTask is not { } task) return;
        IReadOnlyList<(int Row, int Column)> points = _matrix.Propagation(task);
        if (points.Count == 0) return;
        double cw = AreasGeometry.ColumnWidth * _zoom;
        var pen = new Pen(AppColors.Brush(AppColors.Select), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using DrawingContext.PushedState clip = ctx.PushClip(new Rect(AreasGeometry.HeaderWidth, AreasGeometry.TopHeight, Math.Max(0, w - AreasGeometry.HeaderWidth), Math.Max(0, h - AreasGeometry.TopHeight - LegendHeight)));
        Point? previous = null;
        foreach ((int row, int column) in points)
        {
            int line = _lines.Select((x, i) => (x, i)).First(p => p.x.Row == row && p.x.File is null).i;
            var center = new Point(AreasGeometry.ColumnX(column, _scrollX, _zoom) + cw / 2, AreasGeometry.RowY(line, _scrollY) + AreasGeometry.RowHeight / 2);
            if (previous is { } p) ctx.DrawLine(pen, p, center);
            ctx.DrawEllipse(AppColors.Brush(AppColors.Select), null, center, 4, 4);
            previous = center;
        }
    }

    private void PaintTopHeader(DrawingContext ctx, double w)
    {
        (int c0, int c1) = VisibleColumns();
        double cw = AreasGeometry.ColumnWidth * _zoom;
        using DrawingContext.PushedState clip = ctx.PushClip(new Rect(AreasGeometry.HeaderWidth, 0, Math.Max(0, w - AreasGeometry.HeaderWidth), AreasGeometry.TopHeight));
        ctx.FillRectangle(AppColors.Brush(AppColors.Panel), new Rect(AreasGeometry.HeaderWidth, 0, w, AreasGeometry.TopHeight));
        for (int b = 0; b < _matrix.Bands.Count; b++)
        {
            AreaBand band = _matrix.Bands[b];
            if (band.FirstColumn + band.Count - 1 < c0 || band.FirstColumn > c1) continue;
            double x = AreasGeometry.ColumnX(band.FirstColumn, _scrollX, _zoom);
            double width = band.Count * cw;
            bool selected = band.Task.Index == _selectedTask;
            var rect = new Rect(x + 1, 2, width - 2, AreasGeometry.TaskBandHeight - 4);
            ctx.DrawRectangle(AppColors.Brush(selected ? AppColors.Border : AppColors.Card), selected ? new Pen(AppColors.Brush(AppColors.Select), 2) : null, rect, 4, 4);
            AreaTaskSummary summary = _matrix.Snapshot.Summaries[band.Task.Index];
            // The title comes first: in a band too narrow to share its line with the badges they move to the counts line.
            double badgesWidth = AreaBadgesWidth(summary);
            bool badgesBelow = badgesWidth > 0 && width - 12 - badgesWidth < MinTitleWidth;
            PaintBadges(ctx, summary, x + width - 6, badgesBelow ? 21 : 5);
            DrawText(ctx, AreaText.TaskTitle(band.Task), x + 6, 4, 11, AppColors.Text, (int)Math.Max(0, width - 12 - (badgesBelow ? 0 : badgesWidth)), bold: true);
            DrawText(ctx, AreaText.TaskCounts(summary), x + 6, 20, 10, AppColors.Muted, (int)Math.Max(0, width - 12 - (badgesBelow ? badgesWidth : 0)));
        }
        for (int c = c0; c <= c1; c++)
        {
            double x = AreasGeometry.ColumnX(c, _scrollX, _zoom);
            using DrawingContext.PushedState opacity = Dimmed(c) ? ctx.PushOpacity(0.4) : default;
            bool selected = _matrix.Columns[c].Turn == _selectedTurn;
            if (selected) ctx.FillRectangle(AppColors.Brush(AppColors.Border), new Rect(x, AreasGeometry.TaskBandHeight, cw, AreasGeometry.TurnBandHeight));
            if (ColumnFocused(c)) ctx.FillRectangle(FocusBrush(0.45), new Rect(x, AreasGeometry.TaskBandHeight, cw, AreasGeometry.TurnBandHeight));
            DrawText(ctx, (_matrix.Columns[c].Turn + 1).ToString(CultureInfo.CurrentCulture), x + 4, AreasGeometry.TaskBandHeight + 2, 10, selected ? AppColors.Text : AppColors.Muted, (int)Math.Max(0, cw - 6));
        }
    }

    /// <summary>Narrowest title, in pixels, that still shows "Task 51" in full.</summary>
    private const double MinTitleWidth = 56;

    private const double BadgeWidth = 15;

    private static double AreaBadgesWidth(AreaTaskSummary summary) =>
        new[] { AreaBadges.UntestedChange, AreaBadges.CrossModule, AreaBadges.SharedTouched }.Count(badge => summary.Badges.HasFlag(badge)) * BadgeWidth;

    /// <summary>Draws the badges of a task right-aligned ending at <paramref name="right"/>; returns the width they take.</summary>
    private double PaintBadges(DrawingContext ctx, AreaTaskSummary summary, double right, double y)
    {
        (AreaBadges Badge, Codicon Icon, Color Color)[] all =
        [
            (AreaBadges.UntestedChange, Codicon.Question, AppColors.Modified),
            (AreaBadges.CrossModule, Codicon.RepoForked, AppColors.Read),
            (AreaBadges.SharedTouched, Codicon.Link, AppColors.Deleted),
        ];
        double taken = 0;
        foreach ((AreaBadges badge, Codicon icon, Color color) in all)
        {
            if (!summary.Badges.HasFlag(badge)) continue;
            taken += BadgeWidth;
            if (!_glyphs.TryGetValue((icon, color), out FormattedText? glyph))
            {
                glyph = new FormattedText(Codicons.Glyph(icon), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Codicons.Font), 12, AppColors.Brush(color));
                _glyphs[(icon, color)] = glyph;
            }
            ctx.DrawText(glyph, new Point(right - taken + 2, y));
        }
        return taken;
    }

    private void PaintRowHeaders(DrawingContext ctx, double h)
    {
        (int l0, int l1) = VisibleLines();
        using DrawingContext.PushedState clip = ctx.PushClip(new Rect(0, AreasGeometry.TopHeight, AreasGeometry.HeaderWidth, Math.Max(0, h - AreasGeometry.TopHeight - LegendHeight)));
        ctx.FillRectangle(AppColors.Brush(AppColors.Panel), new Rect(0, AreasGeometry.TopHeight, AreasGeometry.HeaderWidth, h));
        for (int l = l0; l <= l1; l++)
        {
            AreaDisplayRow line = _lines[l];
            double y = AreasGeometry.RowY(l, _scrollY);
            bool isFile = line.File is not null;
            bool selected = !isFile && !line.IsMore && _selectedRow == _matrix.Rows[line.Row].Key;
            if (selected) ctx.FillRectangle(AppColors.Brush(AppColors.Card), new Rect(0, y, AreasGeometry.HeaderWidth, AreasGeometry.RowHeight));
            if (LineFocused(line)) ctx.FillRectangle(FocusBrush(0.3), new Rect(0, y, AreasGeometry.HeaderWidth, AreasGeometry.RowHeight));
            if (_hovered.Kind == AreasHitKind.RowHeader && _hovered.Row == l) ctx.DrawRectangle(null, new Pen(AppColors.Brush(AppColors.Select), 1), new Rect(0.5, y + 0.5, AreasGeometry.HeaderWidth - 1, AreasGeometry.RowHeight - 1));
            double indent = isFile || line.IsMore ? 22 : 8;
            if (!isFile && !line.IsMore)
            {
                DrawText(ctx, _expanded.Contains(_matrix.Rows[line.Row].Key) ? "▾" : "▸", 6, y + 4, 11, AppColors.Muted, 0);
                indent = 20;
            }
            Color color = line.IsMore ? AppColors.Muted : isFile ? AppColors.Muted : AppColors.Text;
            DrawText(ctx, line.Label, indent, y + 4, isFile || line.IsMore ? 10 : 12, color, (int)(AreasGeometry.HeaderWidth - indent - 8));
            if (!line.IsMore && !isFile && _matrix.MaxRowValue > 0)
            {
                double bar = (AreasGeometry.HeaderWidth - 28) * line.Total.Value(_matrix.Filter) / _matrix.MaxRowValue;
                HeatHue hue = AreasMatrix.Hue(line.Total, _matrix.Filter);
                Color tone = hue switch { HeatHue.Error => AppColors.Error, HeatHue.Modified => AppColors.Modified, _ => AppColors.Read };
                ctx.FillRectangle(AppColors.Brush(AppColors.Lerp(AppColors.Panel, tone, 0.55)), new Rect(20, y + AreasGeometry.RowHeight - 4, Math.Max(2, bar), 2));
            }
        }
        ctx.DrawLine(new Pen(AppColors.Brush(AppColors.BorderSoft), 1), new Point(AreasGeometry.HeaderWidth, AreasGeometry.TopHeight), new Point(AreasGeometry.HeaderWidth, h));
    }

    /// <summary>The legend, in a strip of its own below the map.</summary>
    private void PaintLegend(DrawingContext ctx, double w, double h)
    {
        double top = h - LegendHeight;
        ctx.FillRectangle(AppColors.Brush(AppColors.Panel), new Rect(0, top, w, LegendHeight));
        double y = top + LegendHeight / 2;
        string[] labels = [MapStrings.LegendModified, MapStrings.LegendRead, MapStrings.LegendError];
        Color[] colors = [AppColors.Modified, AppColors.Read, AppColors.Error];
        double x = 14;
        for (int i = 0; i < labels.Length; i++)
        {
            ctx.DrawRectangle(AppColors.Brush(colors[i]), null, new Rect(x, y - 5, 10, 10));
            DrawText(ctx, labels[i], x + 15, y - 8, 11, AppColors.Muted, 0);
            x += 25 + labels[i].Length * 6.2;
        }
        DrawText(ctx, AreaStrings.LegendNote, x + 10, y - 8, 11, AppColors.Muted, 0);
    }

    /// <summary>Draws the text on one line; with <paramref name="maxWidth"/> greater than 0 it is truncated with an ellipsis.</summary>
    private void DrawText(DrawingContext ctx, string text, double x, double y, double size, Color color, int maxWidth, bool bold = false)
    {
        if (_texts.Count > 3000) _texts.Clear();
        var key = (text, size, color, maxWidth, bold);
        if (!_texts.TryGetValue(key, out FormattedText? formatted))
        {
            formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? SansBold : Sans, size, AppColors.Brush(color));
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
