using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Graph;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>The graph tab: a bar with follow-the-end, zoom and the summary, the canvas and the detail.</summary>
public sealed class GraphPane : UserControl
{
    private readonly FlowCanvasView _canvas = new();
    private readonly TextBlock _summary = new() { Foreground = AppColors.Brush(AppColors.Muted), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _zoomLabel = new() { Text = ZoomText(1.0), MinWidth = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _follow = new();
    private readonly ToggleButton _errorsOnly = new();
    private readonly ToggleButton _hideReads = new();
    private readonly ToggleButton _vertical = new() { IsChecked = true };
    private readonly DetailPane _detailPane;
    private readonly Border _detailHost = new();
    private readonly Action<FilePath> _openFile;
    private StepDetail? _shownDetail;
    private IReadOnlyList<ChangeEntry> _changes = [];

    /// <param name="openFile">Opens a file in the viewer.</param>
    /// <param name="showDiff">Shows its change in the Inspector.</param>
    public GraphPane(Action<FilePath> openFile, Action<FilePath> showDiff)
    {
        _openFile = openFile;
        _detailPane = new DetailPane(openFile, showDiff, GotoLane, () => _canvas.Select(null));
        Button zoomOut = IconButton(Codicon.ZoomOut, GraphStrings.ZoomOutTooltip);
        Button zoomIn = IconButton(Codicon.ZoomIn, GraphStrings.ZoomInTooltip);
        zoomOut.Click += (_, _) => Zoom(1 / 1.25);
        zoomIn.Click += (_, _) => Zoom(1.25);
        _follow.Content = Labelled(Codicon.ArrowRight, GraphStrings.FollowButton);
        ToolTip.SetTip(_follow, GraphStrings.FollowTooltip);
        _follow.Click += (_, _) =>
        {
            _canvas.GoToEnd();
            RefreshBar();
        };
        Button collapseAll = IconButton(Codicon.CollapseAll, GraphStrings.CollapseAllTooltip);
        Button expandAll = IconButton(Codicon.ExpandAll, GraphStrings.ExpandAllTooltip);
        collapseAll.Click += (_, _) => _canvas.CollapseAll();
        expandAll.Click += (_, _) => _canvas.ExpandAll();
        _errorsOnly.Content = Labelled(Codicon.Error, GraphStrings.ErrorsOnlyButton);
        ToolTip.SetTip(_errorsOnly, GraphStrings.ErrorsOnlyTooltip);
        _hideReads.Content = Labelled(Codicon.EyeClosed, GraphStrings.HideReadsButton);
        ToolTip.SetTip(_hideReads, GraphStrings.HideReadsTooltip);
        _errorsOnly.Click += (_, _) => ApplyFilter();
        _hideReads.Click += (_, _) => ApplyFilter();
        _vertical.Content = Labelled(Codicon.ArrowDown, GraphStrings.VerticalButton);
        ToolTip.SetTip(_vertical, GraphStrings.VerticalTooltip);
        _vertical.Click += (_, _) =>
        {
            _canvas.SetOrientation(_vertical.IsChecked == true ? GraphOrientation.Vertical : GraphOrientation.Horizontal);
            RefreshBar();
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _follow, _vertical, collapseAll, expandAll, _errorsOnly, _hideReads } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _summary, zoomOut, _zoomLabel, zoomIn } };
        var bar = new DockPanel { Margin = new Thickness(8, 4) };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(left);

        var content = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        content.Children.Add(bar);
        DockPanel.SetDock(_detailHost, Dock.Right);
        content.Children.Add(_detailHost);
        content.Children.Add(_canvas);
        Content = content;

        _canvas.ViewChanged += RefreshBar;
        _canvas.Selected += _ => RefreshDetail();
        _canvas.Activated += Activate;
        RefreshBar();
    }

    public FlowCanvasView Canvas => _canvas;

    public DetailPane Detail => _detailPane;

    public bool DetailVisible => _shownDetail is not null;

    public string Summary => _summary.Text ?? "";

    /// <summary>The changes git attributes to each action: they complete the files of a step.</summary>
    public void SetChanges(IReadOnlyList<ChangeEntry> changes)
    {
        _changes = changes;
        RefreshDetail();
    }

    public void SetGraph(FlowGraph graph)
    {
        _canvas.SetGraph(graph);
        RefreshBar();
        RefreshDetail();
    }

    /// <summary>Sets the orientation as if the user had pressed the button (<c>--graph-orientation</c>).</summary>
    public void ShowOrientation(GraphOrientation orientation)
    {
        _vertical.IsChecked = orientation.IsVertical();
        _canvas.SetOrientation(orientation);
        RefreshBar();
    }

    /// <summary>Sets the filters as if the user had pressed the buttons (<c>--graph-filter</c>).</summary>
    public void ShowFilter(GraphFilter filter)
    {
        _errorsOnly.IsChecked = filter.OnlyErrors;
        _hideReads.IsChecked = filter.HideReads;
        _canvas.SetFilter(filter);
    }

    private void ApplyFilter() => _canvas.SetFilter(new GraphFilter(_errorsOnly.IsChecked == true, _hideReads.IsChecked == true));

    /// <summary>Enter: opens the first file the step touched.</summary>
    private void Activate(GraphSelection selection)
    {
        StepDetail? detail = StepDetail.Of(_canvas.Graph, selection, TimeZoneInfo.Local, _changes);
        if (detail?.Files.FirstOrDefault() is { } file) _openFile(file.Path);
    }

    /// <summary>Jumps to the first step of a subagent.</summary>
    private void GotoLane(string laneId)
    {
        FlowGraph.Batch? first = _canvas.Graph.BatchesOf(laneId).FirstOrDefault();
        if (first is { Steps.Count: > 0 }) _canvas.Select(GraphSelection.ForStep(first.Steps[0].CallId));
    }

    /// <summary>The panel is rebuilt only if what it shows has changed, so the user's text selection is not lost.</summary>
    private void RefreshDetail()
    {
        StepDetail? detail = StepDetail.Of(_canvas.Graph, _canvas.Selection, TimeZoneInfo.Local, _changes);
        if (Equals(detail, _shownDetail)) return;
        _shownDetail = detail;
        if (detail is null)
        {
            _detailHost.Child = null;
            return;
        }
        _detailPane.Show(detail);
        _detailHost.Child = _detailPane;
    }

    private void Zoom(double factor)
    {
        _canvas.SetZoom(_canvas.Viewport.Zoom * factor);
        RefreshBar();
    }

    private void RefreshBar()
    {
        FlowGraph g = _canvas.Graph;
        var parts = new List<string>
        {
            Count(g.StepCount, GraphStrings.CountSteps_One, GraphStrings.CountSteps_Other),
            Count(g.ErrorCount, GraphStrings.CountErrors_One, GraphStrings.CountErrors_Other),
            Count(g.Turns.Count, GraphStrings.CountTurns_One, GraphStrings.CountTurns_Other),
        };
        if (g.Lanes.Count > 1) parts.Add(Count(g.Lanes.Count - 1, GraphStrings.CountSubagents_One, GraphStrings.CountSubagents_Other));
        _summary.Text = string.Join(" · ", parts);
        _zoomLabel.Text = ZoomText(_canvas.Viewport.Zoom);
        _follow.IsEnabled = !_canvas.Viewport.Following;
    }

    private static string Count(int n, string one, string other) => string.Format(CultureInfo.CurrentCulture, Plural.Pick(n, one, other), n);

    private static string ZoomText(double zoom) => string.Format(CultureInfo.CurrentCulture, GraphStrings.ZoomPercentFormat, Math.Round(zoom * 100));

    private static Control Labelled(Codicon icon, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new TextBlock { Text = Codicons.Glyph(icon), FontFamily = Codicons.Font, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    private static Button IconButton(Codicon icon, string tooltip)
    {
        var button = new Button { Content = new TextBlock { Text = Codicons.Glyph(icon), FontFamily = Codicons.Font, FontSize = 14 } };
        ToolTip.SetTip(button, tooltip);
        return button;
    }
}
