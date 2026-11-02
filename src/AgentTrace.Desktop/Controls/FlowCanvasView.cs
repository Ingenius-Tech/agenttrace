using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AgentTrace.Core.Flow;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Graph;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;
using Band = AgentTrace.Presentation.Graph.GraphGeometry.Band;
using Box = AgentTrace.Presentation.Graph.GraphGeometry.Box;
using Edge = AgentTrace.Presentation.Graph.GraphGeometry.Edge;
using GRect = AgentTrace.Presentation.Graph.GraphGeometry.Rect;
using Kind = AgentTrace.Presentation.Graph.GraphGeometry.Kind;
using Row = AgentTrace.Presentation.Graph.GraphGeometry.Row;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Draws the execution graph in a single control the size of the view: only the visible boxes are painted, so the cost
/// does not grow with the session. The layout and the view are pure logic (<c>Presentation.Graph</c>); here we only paint
/// and translate the wheel, dragging, the keyboard and the scroll bars.
/// </summary>
public sealed class FlowCanvasView : Grid
{
    private const int MaxBoxesPerFrame = 1_500;
    private const double Arc = 8;
    private const double DimAlpha = 0.28;

    /// <summary>Height of the header of each turn band (when horizontal): a click on it folds or unfolds it.</summary>
    private const double HeaderHeight = 30;

    /// <summary>A call without a result for longer than this is no longer considered running (the session was cut).</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(30);

    private static readonly Typeface Sans = new("Segoe UI, Inter, Helvetica, Arial, sans-serif");
    private static readonly Typeface SansBold = new("Segoe UI, Inter, Helvetica, Arial, sans-serif", FontStyle.Normal, FontWeight.Bold, FontStretch.Normal);

    private readonly Surface _surface;
    private readonly ScrollBar _hBar = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollBar _vBar = new() { Orientation = Orientation.Vertical };
    private readonly GraphViewport _viewport = new();
    private readonly TurnFolding _folding = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<(string Text, double Size, Color Color, bool Bold), FormattedText> _texts = [];

    private FlowGraph _graph = new FlowBuilder().Snapshot();
    private GraphFilter _filter = GraphFilter.None;
    private IReadOnlySet<int> _collapsed = new HashSet<int>();
    private int[] _turnSteps = [];
    private int[] _turnErrors = [];
    private GraphGeometry _geometry;
    private Dictionary<string, int> _laneStart = [];
    private GraphOrientation _orientation = GraphOrientation.Vertical;
    private GraphSelection? _selection;
    private bool _syncingBars;
    private bool _dragged;
    private Point _dragFrom;
    private Box? _tipBox;

    public FlowCanvasView()
    {
        _geometry = FlowLayout.Of(_graph, new HashSet<int>(), _orientation);
        _viewport.SetVertical(_orientation.IsVertical());
        _surface = new Surface(this) { Focusable = true, ClipToBounds = true };
        RowDefinitions = new RowDefinitions("*,Auto");
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        Children.Add(_surface);
        Grid.SetRow(_hBar, 1);
        Children.Add(_hBar);
        Grid.SetColumn(_vBar, 1);
        Children.Add(_vBar);
        _hBar.ValueChanged += (_, _) => OnBar();
        _vBar.ValueChanged += (_, _) => OnBar();
        _clock.Tick += (_, _) => _surface.InvalidateVisual();
        _surface.PointerWheelChanged += OnWheel;
        _surface.PointerPressed += OnPressed;
        _surface.PointerMoved += OnMoved;
        _surface.PointerReleased += OnReleased;
        _surface.PointerExited += (_, _) => HideTip();
        _surface.KeyDown += OnKey;
        _surface.SizeChanged += (_, e) =>
        {
            _viewport.SetViewSize(e.NewSize.Width, e.NewSize.Height);
            UpdateView();
        };
    }

    // ---- API ----

    /// <summary>Raised when the selection changes (click, keyboard or programmatic); <c>null</c> means nothing is selected.</summary>
    public event Action<GraphSelection?>? Selected;

    /// <summary>Enter on the selection.</summary>
    public event Action<GraphSelection>? Activated;

    /// <summary>The view (zoom, scroll) or the content has changed.</summary>
    public event Action? ViewChanged;

    public FlowGraph Graph => _graph;

    public GraphGeometry Geometry => _geometry;

    public GraphViewport Viewport => _viewport;

    public GraphSelection? Selection => _selection;

    public GraphOrientation FlowOrientation => _orientation;

    public Control Surface_ => _surface;

    /// <summary>Replaces the graph (called often when live: the layout, which is linear, is recomputed).</summary>
    public void SetGraph(FlowGraph graph)
    {
        _graph = graph;
        Relayout();
        bool running = _graph.Batches.SelectMany(b => b.Steps).Any(s => s.State == StepState.Running
            && s.Start is { } start && DateTimeOffset.UtcNow - start < Stale);
        if (running && !_clock.IsEnabled) _clock.Start();
        else if (!running) _clock.Stop();
        UpdateView();
    }

    /// <summary>Changes where time advances; the view goes to the end of the new axis and follows the session.</summary>
    public void SetOrientation(GraphOrientation value)
    {
        if (_orientation == value) return;
        _orientation = value;
        _viewport.SetVertical(value.IsVertical());
        Relayout();
        if (BoxOf(_selection) is { } box) _viewport.Reveal(box, _geometry.ContentEnd);
        UpdateView();
    }

    /// <summary>Filters that dim what is not of interest; the graph structure does not change.</summary>
    public void SetFilter(GraphFilter value)
    {
        _filter = value;
        UpdateView();
    }

    /// <summary><c>false</c>: no automatic folding (everything unfolded until the user folds).</summary>
    public void SetAutoFold(bool auto)
    {
        _folding.SetAuto(auto);
        Relayout();
        UpdateView();
    }

    public void CollapseAll()
    {
        _folding.CollapseAll(_graph);
        Relayout();
        UpdateView();
    }

    public void ExpandAll()
    {
        _folding.ExpandAll(_graph);
        Relayout();
        UpdateView();
    }

    /// <summary>Selects a step or a turn and brings it into view. Null clears the selection.</summary>
    public void Select(GraphSelection? value)
    {
        _selection = value;
        Relayout(); // the turn of the selection unfolds and the one left behind, unless pinned by the user, folds
        if (BoxOf(value) is { } box) _viewport.Reveal(box, _geometry.ContentEnd);
        UpdateView();
        Selected?.Invoke(value);
    }

    public void SetZoom(double zoom)
    {
        _viewport.SetZoom(zoom, _surface.Bounds.Width / 2, _surface.Bounds.Height / 2);
        UpdateView();
    }

    public void GoToEnd()
    {
        _viewport.GoToEnd();
        UpdateView();
    }

    // ---- layout ----

    /// <summary>Recomputes the layout with the turns that must be folded and the figures of each turn (for its summary card).</summary>
    private void Relayout()
    {
        _collapsed = _folding.Collapsed(_graph, _selection);
        _geometry = FlowLayout.Of(_graph, _collapsed, _orientation);
        Dictionary<string, int> starts = [];
        foreach (Box b in _geometry.Boxes)
        {
            int along = _orientation.IsVertical() ? b.Y : b.X;
            starts[b.LaneId] = starts.TryGetValue(b.LaneId, out int current) ? Math.Min(current, along) : along;
        }
        _laneStart = starts;
        _turnSteps = new int[_graph.Turns.Count];
        _turnErrors = new int[_graph.Turns.Count];
        foreach (Batch batch in _graph.Batches)
        {
            int t = Math.Min(batch.TurnIndex, _graph.Turns.Count - 1);
            if (t < 0) continue;
            foreach (Step s in batch.Steps)
            {
                _turnSteps[t]++;
                if (s.State == StepState.Error) _turnErrors[t]++;
            }
        }
        _viewport.SetContentSize(_geometry.Width, _geometry.Height);
    }

    private static Rect ToRect(GRect r) => new(r.X, r.Y, r.W, r.H);

    private Box? BoxOf(GraphSelection? s)
    {
        if (s is null) return null;
        if (s.IsStep) return _geometry.FindStep(s.CallId!);
        return _geometry.FindTurnBox(s.TurnIndex, Kind.Prompt) ?? _geometry.FindTurnBox(s.TurnIndex, Kind.Answer)
            ?? _geometry.FindTurnBox(s.TurnIndex, Kind.Folded);
    }

    private bool Dimmed(Box box)
    {
        if (!_filter.Active) return false;
        return box.Kind switch
        {
            Kind.Step => _filter.Dims(_graph.Batches[box.BatchIndex].Steps[box.StepIndex]),
            Kind.Folded => _filter.DimsFoldedTurn(_turnErrors[box.TurnIndex]),
            Kind.Prompt or Kind.Answer => false,
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }

    private void ToggleTurn(int turn)
    {
        _folding.Toggle(turn, _graph, _selection);
        Relayout();
        UpdateView();
    }

    private void UpdateView()
    {
        _syncingBars = true;
        Rect view = ToRect(_viewport.VisibleRect());
        _hBar.Maximum = _viewport.MaxScrollX();
        _hBar.ViewportSize = view.Width;
        _hBar.Value = _viewport.ScrollX;
        _vBar.Maximum = _viewport.MaxScrollY();
        _vBar.ViewportSize = view.Height;
        _vBar.Value = _viewport.ScrollY;
        _syncingBars = false;
        _surface.InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private void OnBar()
    {
        if (_syncingBars) return;
        _viewport.ScrollTo(_hBar.Value, _vBar.Value);
        _surface.InvalidateVisual();
    }

    // ---- input ----

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        Point p = e.GetPosition(_surface);
        const double notch = 40; // Avalonia gives 1 per notch; the Java version, about 40 pixels
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _viewport.ZoomBy(e.Delta.Y > 0 ? 1.1 : 1 / 1.1, p.X, p.Y);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            double d = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            _viewport.ScrollBy(-d * notch / _viewport.Zoom, 0);
        }
        else
        {
            _viewport.ScrollBy(-e.Delta.X * notch / _viewport.Zoom, -e.Delta.Y * notch / _viewport.Zoom);
        }
        UpdateView();
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _surface.Focus();
        _dragFrom = e.GetPosition(_surface);
        _dragged = false;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        Point p = e.GetPosition(_surface);
        if (e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(p.X - _dragFrom.X) + Math.Abs(p.Y - _dragFrom.Y) > 3) _dragged = true;
            _viewport.ScrollBy((_dragFrom.X - p.X) / _viewport.Zoom, (_dragFrom.Y - p.Y) / _viewport.Zoom);
            _dragFrom = p;
            UpdateView();
            return;
        }
        Box? hit = _geometry.Hit(_viewport.ToWorldX(p.X), _viewport.ToWorldY(p.Y));
        _surface.Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (hit is null)
        {
            HideTip();
            return;
        }
        if (!hit.Equals(_tipBox))
        {
            _tipBox = hit;
            ToolTip.SetTip(_surface, TipText(hit));
        }
    }

    private void HideTip()
    {
        _tipBox = null;
        ToolTip.SetTip(_surface, null);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged || e.InitialPressMouseButton != MouseButton.Left) return;
        Point p = e.GetPosition(_surface);
        double wx = _viewport.ToWorldX(p.X);
        double wy = _viewport.ToWorldY(p.Y);
        Box? hit = _geometry.Hit(wx, wy);
        bool vertical = _orientation.IsVertical();
        if (hit is null && (vertical ? wx < FlowLayout.HeaderV - 10 : wy < HeaderHeight))
        {
            // The band header folds or unfolds its turn
            if (_geometry.BandAt(vertical ? wy : wx) is { } band) ToggleTurn(band.TurnIndex);
            return;
        }
        if (hit is { Kind: Kind.Folded }) _folding.Toggle(hit.TurnIndex, _graph, _selection);
        Select(hit is null ? null : GraphSelection.Of(hit));
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        GraphNavigator.Move? move = e.Key switch
        {
            Key.Left => GraphNavigator.Move.Left,
            Key.Right => GraphNavigator.Move.Right,
            Key.Up => GraphNavigator.Move.Up,
            Key.Down => GraphNavigator.Move.Down,
            Key.Home => GraphNavigator.Move.First,
            Key.End => GraphNavigator.Move.Last,
            Key.E => GraphNavigator.Move.NextError,
            _ => null,
        };
        if (move is { } m)
        {
            if (GraphNavigator.MoveFrom(_geometry, BoxOf(_selection), m) is { } box) Select(GraphSelection.Of(box));
            if (m == GraphNavigator.Move.Last) GoToEnd();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _selection is not null)
        {
            Activated?.Invoke(_selection);
            e.Handled = true;
        }
    }

    // ---- texts ----

    private string TipText(Box box)
    {
        if (box.Kind == Kind.Step)
        {
            Step step = _graph.Batches[box.BatchIndex].Steps[box.StepIndex];
            string summary = string.IsNullOrWhiteSpace(step.Summary) ? "" : "\n" + ClipText(step.Summary);
            return step.ToolName + summary + "\n" + StepText.Status(step, DateTimeOffset.UtcNow);
        }
        if (box.Kind == Kind.Folded) return Title(box, null) + "\n" + Subtitle(box, null, DateTimeOffset.UtcNow) + "\n" + GraphStrings.FoldedClickToExpand;
        Turn turn = _graph.Turns[box.TurnIndex];
        return ClipText(box.Kind == Kind.Prompt ? turn.Prompt : turn.Answer);
    }

    private static string ClipText(string? text)
    {
        string t = text?.Trim() ?? "";
        return t.Length <= 300 ? t : t[..300] + "…";
    }

    private string Title(Box box, Step? step)
    {
        if (step is not null)
        {
            string text = FirstLine(string.IsNullOrWhiteSpace(step.Summary) ? step.ToolName : step.Summary);
            // A card has little room: for a path the file name matters, not the beginning
            if (step.Kind is ToolKind.Read or ToolKind.Write)
            {
                int cut = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
                if (cut >= 0) text = text[(cut + 1)..];
            }
            return text;
        }
        Turn turn = _graph.Turns[box.TurnIndex];
        if (box.Kind == Kind.Folded)
        {
            return turn.Prompt is null
                ? string.Format(CultureInfo.CurrentCulture, GraphStrings.TurnContinuationTitleFormat, turn.Index + 1)
                : string.Format(CultureInfo.CurrentCulture, GraphStrings.TurnPromptTitleFormat, turn.Index + 1, FirstLine(turn.Prompt));
        }
        return FirstLine(box.Kind == Kind.Prompt ? turn.Prompt : turn.Answer);
    }

    private string Subtitle(Box box, Step? step, DateTimeOffset now)
    {
        if (step is not null) return step.ToolName + " · " + StepText.Status(step, now);
        if (box.Kind == Kind.Folded)
        {
            int t = box.TurnIndex;
            Turn turn = _graph.Turns[t];
            return Count(_turnSteps[t], GraphStrings.CountSteps_One, GraphStrings.CountSteps_Other)
                   + (_turnErrors[t] > 0 ? " · " + Count(_turnErrors[t], GraphStrings.CountErrors_One, GraphStrings.CountErrors_Other) : "")
                   + (turn.Duration is null ? "" : " · " + StepText.FormatDuration(turn.Duration));
        }
        return box.Kind == Kind.Prompt ? GraphStrings.PromptLabel : GraphStrings.AnswerLabel;
    }

    private static string Count(int n, string one, string other) => string.Format(CultureInfo.CurrentCulture, Plural.Pick(n, one, other), n);

    private static string FirstLine(string? text)
    {
        if (text is null) return "";
        int nl = text.IndexOf('\n');
        return nl < 0 ? text : text[..nl];
    }

    // ---- painting ----

    private void Paint(DrawingContext ctx)
    {
        double w = _surface.Bounds.Width;
        double h = _surface.Bounds.Height;
        ctx.FillRectangle(AppColors.Brush(AppColors.Background), new Rect(0, 0, w, h));
        if (_geometry.Boxes.Count == 0)
        {
            DrawText(ctx, GraphStrings.GraphEmpty, 24, 26, 14, AppColors.Muted);
            return;
        }
        double zoom = _viewport.Zoom;
        bool detailed = _viewport.Detailed;
        Rect view = ToRect(_viewport.VisibleRect());
        using (ctx.PushTransform(Matrix.CreateTranslation(-_viewport.ScrollX, -_viewport.ScrollY) * Matrix.CreateScale(zoom, zoom)))
        {
            PaintBands(ctx, view, detailed);
            PaintLaneLabels(ctx, view, detailed);
            PaintEdges(ctx, view);
            int drawn = 0;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (Box box in _geometry.Visible(_viewport.VisibleRect()))
            {
                if (drawn++ >= MaxBoxesPerFrame) break;
                using (ctx.PushOpacity(Dimmed(box) ? DimAlpha : 1.0)) PaintBox(ctx, box, detailed, now);
            }
            if (BoxOf(_selection) is { } selected)
            {
                var pen = new Pen(AppColors.Brush(AppColors.Select), 2.5);
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(selected.X - 4, selected.Y - 4, selected.W + 8, selected.H + 8), Arc + 3));
            }
        }
    }

    private void PaintBands(DrawingContext ctx, Rect view, bool detailed)
    {
        bool v = _orientation.IsVertical();
        foreach (Band band in _geometry.Bands)
        {
            double from = v ? view.Y : view.X;
            double to = v ? view.Bottom : view.Right;
            if (band.Start + band.Length < from || band.Start > to) continue;
            if (band.TurnIndex % 2 == 1)
            {
                IBrush fill = AppColors.Brush(AppColors.Panel);
                ctx.FillRectangle(fill, v ? new Rect(view.X, band.Start, view.Width, band.Length) : new Rect(band.Start, view.Y, band.Length, view.Height));
            }
            if (!detailed || band.TurnIndex >= _graph.Turns.Count) continue;
            Turn turn = _graph.Turns[band.TurnIndex];
            List<string> parts = [string.Format(CultureInfo.CurrentCulture, GraphStrings.TurnTitleFormat, turn.Index + 1)];
            if (turn.Prompt is null) parts.Add(GraphStrings.TurnPartContinuation);
            if (!turn.Closed) parts.Add(GraphStrings.TurnPartUnclosed);
            if (turn.Duration is not null) parts.Add(StepText.FormatDuration(turn.Duration));
            Codicon chevron = _collapsed.Contains(band.TurnIndex) ? Codicon.ChevronRight : Codicon.ChevronDown;
            Rect clip = v ? new Rect(0, band.Start, FlowLayout.HeaderV - 8, band.Length) : new Rect(band.Start, 0, band.Length, HeaderHeight);
            using (ctx.PushClip(clip))
            {
                // The label follows the view while the band is in view; the chevron says whether the turn is folded
                if (v)
                {
                    double labelY = Math.Max(band.Start + 16, Math.Min(view.Y + 16, band.Start + band.Length - 4 - 14 * (parts.Count - 1)));
                    DrawIcon(ctx, chevron, 17, labelY - 4, 11, AppColors.Muted);
                    for (int i = 0; i < parts.Count; i++) DrawText(ctx, parts[i], 28, labelY + 14 * i - 11, 11, AppColors.Muted);
                }
                else
                {
                    double labelX = Math.Max(band.Start + 12, Math.Min(view.X + 8, band.Start + band.Length - 120));
                    DrawIcon(ctx, chevron, labelX + 5, 16, 11, AppColors.Muted);
                    DrawText(ctx, string.Join(" · ", parts), labelX + 14, 9, 11, AppColors.Muted);
                }
            }
        }
    }

    private void PaintLaneLabels(DrawingContext ctx, Rect view, bool detailed)
    {
        if (!detailed) return;
        bool v = _orientation.IsVertical();
        double from = v ? view.Y : view.X;
        double to = v ? view.Bottom : view.Right;
        foreach (Lane lane in _graph.Lanes)
        {
            if (lane.IsMain || !_laneStart.TryGetValue(lane.Id, out int start)) continue;
            foreach (Row row in _geometry.Rows)
            {
                if (!row.LaneIds.Contains(lane.Id) || start + 400 <= from || start >= to) continue;
                if (v) DrawText(ctx, "↳ " + lane.Title, row.Start, start - 20, 11, AppColors.Muted);
                else DrawText(ctx, "↳ " + lane.Title, start, row.Start - 20, 11, AppColors.Muted);
            }
        }
    }

    private void PaintEdges(DrawingContext ctx, Rect view)
    {
        bool v = _orientation.IsVertical();
        foreach (Edge e in _geometry.Edges)
        {
            double lo = v ? Math.Min(e.Y1, e.Y2) : Math.Min(e.X1, e.X2);
            double hi = v ? Math.Max(e.Y1, e.Y2) : Math.Max(e.X1, e.X2);
            if (hi < (v ? view.Y : view.X) || lo > (v ? view.Bottom : view.Right)) continue;
            var pen = new Pen(AppColors.Brush(AppColors.Border), 1.2, e.Dashed ? new DashStyle([4.2, 3.3], 0) : null, PenLineCap.Round);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext g = geometry.Open())
            {
                g.BeginFigure(new Point(e.X1, e.Y1), false);
                if (v)
                {
                    double mid = (e.Y1 + e.Y2) / 2.0;
                    g.CubicBezierTo(new Point(e.X1, mid), new Point(e.X2, mid), new Point(e.X2, e.Y2));
                }
                else
                {
                    double mid = (e.X1 + e.X2) / 2.0;
                    g.CubicBezierTo(new Point(mid, e.Y1), new Point(mid, e.Y2), new Point(e.X2, e.Y2));
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geometry);
            if (e.Label is not null)
            {
                if (v) DrawText(ctx, e.Label, e.X1 + 8, (e.Y1 + e.Y2) / 2.0 - 6, 10, AppColors.Muted);
                else DrawText(ctx, e.Label, (e.X1 + e.X2) / 2.0 - 22, e.Y1 - 18, 10, AppColors.Muted);
            }
        }
    }

    private void PaintBox(DrawingContext ctx, Box box, bool detailed, DateTimeOffset now)
    {
        Step? step = box.Kind == Kind.Step ? _graph.Batches[box.BatchIndex].Steps[box.StepIndex] : null;
        // A folded turn with errors shows it on its border, like a step with an error
        StepState state = step is not null ? step.State
            : box.Kind == Kind.Folded && _turnErrors[box.TurnIndex] > 0 ? StepState.Error : StepState.Ok;
        Pen pen = state switch
        {
            StepState.Error => new Pen(AppColors.Brush(AppColors.Deleted), 2),
            StepState.Running => new Pen(AppColors.Brush(AppColors.Read), 1.5, new DashStyle([3.3, 2], 0)),
            StepState.NoResult => new Pen(AppColors.Brush(AppColors.Muted), 1.5, new DashStyle([1, 2], 0)),
            StepState.Ok => new Pen(AppColors.Brush(AppColors.Border), 1),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
        var rounded = new RoundedRect(new Rect(box.X, box.Y, box.W, box.H), Arc);
        ctx.DrawRectangle(AppColors.Brush(AppColors.Card), pen, rounded);

        Codicon icon = box.Kind switch
        {
            Kind.Prompt => Codicon.Person,
            Kind.Answer => Codicon.Comment,
            Kind.Folded => Codicon.ChevronRight,
            Kind.Step => StepIconNames.Of(step!.Kind, step.Files.Any(f => f.Kind is { } k && StepIconNames.Creates(k))),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
        Color iconColor = step is null ? AppColors.Text : step.Kind switch
        {
            ToolKind.Read => AppColors.Read,
            ToolKind.Write => AppColors.Modified,
            _ => AppColors.Text,
        };
        if (!detailed)
        {
            DrawIcon(ctx, icon, box.X + box.W / 2.0, box.CenterY, 22, iconColor);
            return;
        }
        DrawIcon(ctx, icon, box.X + 17, box.CenterY, 16, iconColor);
        if (step is not null)
        {
            DrawIcon(ctx, StepIconNames.Of(state), box.Right - 14, box.Y + 14, 12,
                state == StepState.Error ? AppColors.Deleted : state == StepState.Ok ? AppColors.Created : AppColors.Muted);
        }
        using (ctx.PushClip(new Rect(box.X + 32, box.Y, box.W - 32 - (step is not null ? 24 : 6), box.H)))
        {
            DrawText(ctx, Title(box, step), box.X + 32, box.Y + 5, 12, AppColors.Text);
            DrawText(ctx, Subtitle(box, step, now), box.X + 32, box.Y + 24, 10.5, state == StepState.Error ? AppColors.Deleted : AppColors.Muted);
        }
    }

    /// <summary>Text with its top-left corner at (x, y). Already formatted texts are cached: that is the costly part.</summary>
    private void DrawText(DrawingContext ctx, string text, double x, double y, double size, Color color)
    {
        if (_texts.Count > 4000) _texts.Clear();
        var key = (text, size, color, false);
        if (!_texts.TryGetValue(key, out FormattedText? formatted))
        {
            formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Sans, size, AppColors.Brush(color));
            _texts[key] = formatted;
        }
        ctx.DrawText(formatted, new Point(x, y));
    }

    /// <summary>An icon of the codicon font centered at (cx, cy).</summary>
    private void DrawIcon(DrawingContext ctx, Codicon icon, double cx, double cy, double size, Color color)
    {
        if (_texts.Count > 4000) _texts.Clear();
        string glyph = Codicons.Glyph(icon);
        var key = (glyph, size, color, true);
        if (!_texts.TryGetValue(key, out FormattedText? formatted))
        {
            formatted = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Codicons.Font), size, AppColors.Brush(color));
            _texts[key] = formatted;
        }
        ctx.DrawText(formatted, new Point(cx - formatted.Width / 2, cy - formatted.Height / 2));
    }

    private sealed class Surface(FlowCanvasView owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            owner.Paint(context);
        }
    }
}
