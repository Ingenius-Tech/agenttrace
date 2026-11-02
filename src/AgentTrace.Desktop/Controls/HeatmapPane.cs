using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// The heatmap tab: filters by kind of activity, the area selector, breadcrumbs to go back up, the canvas,
/// the summary and the panel of the selected file (the same as in "Knowledge").
/// </summary>
public sealed class HeatmapPane : UserControl
{
    private readonly HeatmapCanvasView _canvas;
    private readonly FileDetailPanel _detail;
    private readonly ToggleButton _reads = new() { Content = FileDetailPanel.LabelledContent(Codicon.Eye, MapStrings.HeatReads), IsChecked = true };
    private readonly ToggleButton _writes = new() { Content = FileDetailPanel.LabelledContent(Codicon.Diff, MapStrings.HeatWrites), IsChecked = true };
    private readonly ToggleButton _errors = new() { Content = FileDetailPanel.LabelledContent(Codicon.Close, MapStrings.HeatErrors), IsChecked = true };
    private readonly ComboBox _area = new() { ItemsSource = new[] { MapStrings.HeatAreaSize, MapStrings.HeatAreaActivity }, SelectedIndex = 0 };
    private readonly WrapPanel _crumbs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _summary = new() { Text = MapStrings.NoFilesYet, Foreground = AppColors.Brush(AppColors.Muted), TextWrapping = TextWrapping.Wrap };
    private FilePath? _projectRoot;
    private KnowledgeGraph? _graph;

    /// <param name="openFile">Opens a file in the viewer.</param>
    /// <param name="showDiff">Shows its change in the Inspector.</param>
    /// <param name="reveal">Locates it in the project tree.</param>
    /// <param name="showInKnowledge">Selects it in "Knowledge".</param>
    public HeatmapPane(Action<FilePath> openFile, Action<FilePath> showDiff, Action<FilePath> reveal, Action<FilePath> showInKnowledge)
    {
        _canvas = new HeatmapCanvasView(openFile, showDiff, reveal, showInKnowledge);
        _detail = new FileDetailPanel(openFile, showDiff, path => _canvas.Reveal(path), () => _canvas.Select(null));

        ToolTip.SetTip(_reads, MapStrings.HeatReadsTip);
        ToolTip.SetTip(_writes, MapStrings.HeatWritesTip);
        ToolTip.SetTip(_errors, MapStrings.HeatErrorsTip);
        ToolTip.SetTip(_area, MapStrings.HeatAreaTip);
        foreach (ToggleButton button in new[] { _reads, _writes, _errors }) button.Click += (_, e) => OnFilterClicked((ToggleButton)e.Source!);
        _area.SelectionChanged += (_, _) => _canvas.SetArea(_area.SelectedIndex == 1 ? HeatArea.Activity : HeatArea.Size);

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _reads, _writes, _errors, new TextBlock { Text = MapStrings.HeatAreaLabel, Foreground = AppColors.Brush(AppColors.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }, _area },
        };
        var bar = new StackPanel { Margin = new Thickness(8, 4), Spacing = 4, Children = { left, _crumbs } };
        var footer = new Border { Padding = new Thickness(10, 4), Background = AppColors.Brush(AppColors.Panel), Child = _summary };

        var content = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        content.Children.Add(bar);
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        DockPanel.SetDock(_detail, Dock.Right);
        content.Children.Add(_detail);
        content.Children.Add(_canvas);
        Content = content;

        _canvas.Selected += _ => RefreshDetail();
        _canvas.ZoomChanged += RefreshCrumbs;
        RefreshCrumbs();
    }

    public HeatmapCanvasView Canvas => _canvas;

    public bool DetailVisible => _detail.HasDetail;

    public IEnumerable<string> DetailTexts => _detail.Texts;

    public string Summary => _summary.Text ?? "";

    public bool ErrorsEnabled => _errors.IsEnabled;

    public string? ErrorsTooltip => ToolTip.GetTip(_errors) as string;

    /// <summary>The names of the folders in the breadcrumbs, from the root to the current view.</summary>
    public IEnumerable<string> Crumbs => _crumbs.Children.OfType<Button>().Select(b => b.Content as string ?? "");

    public void SetProjectRoot(FilePath? root) => _projectRoot = root;

    /// <summary>Updates the map with a new tree (called often while live) and the graph the detail comes from.</summary>
    public void SetTree(HeatTree tree, KnowledgeGraph graph)
    {
        _graph = graph;
        bool errorsKnown = tree.TargetsKnown;
        _errors.IsEnabled = errorsKnown;
        ToolTip.SetTip(_errors, errorsKnown ? MapStrings.HeatErrorsTip : HeatText.ErrorsUnavailable);
        _canvas.SetFilter(EffectiveFilter());
        _canvas.SetTree(tree);
        _summary.Text = HeatText.Summary(tree, _canvas.Filter);
        RefreshDetail();
    }

    /// <summary>Empties the map (when opening another project or switching session).</summary>
    public void Clear()
    {
        _graph = null;
        _canvas.SetTree(null);
        _summary.Text = MapStrings.NoFilesYet;
        RefreshDetail();
    }

    /// <summary>The chosen kinds of activity (those of <c>--heatmap-filter</c> too).</summary>
    public void SetFilter(ActivityFilter filter)
    {
        _reads.IsChecked = filter.Reads;
        _writes.IsChecked = filter.Writes;
        _errors.IsChecked = filter.Errors;
        ApplyFilter();
    }

    public void SetArea(HeatArea area)
    {
        _area.SelectedIndex = area == HeatArea.Activity ? 1 : 0;
        _canvas.SetArea(area);
    }

    /// <summary>Without calls with a known target "Errors" cannot be counted: it is ignored even if chosen.</summary>
    private ActivityFilter EffectiveFilter() => new(_reads.IsChecked == true, _writes.IsChecked == true, _errors.IsChecked == true && _errors.IsEnabled);

    private void OnFilterClicked(ToggleButton clicked)
    {
        // At least one kind always stays active
        if (_reads.IsChecked != true && _writes.IsChecked != true && _errors.IsChecked != true) clicked.IsChecked = true;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _canvas.SetFilter(EffectiveFilter());
        if (_canvas.Tree is { } tree) _summary.Text = HeatText.Summary(tree, _canvas.Filter);
    }

    private void RefreshCrumbs()
    {
        _crumbs.Children.Clear();
        if (_canvas.Tree is not { } tree || _canvas.Zoom is not { } zoom)
        {
            _crumbs.IsVisible = false;
            return;
        }
        IReadOnlyList<HeatNode> chain = TreemapLayout.Breadcrumb(tree, zoom);
        _crumbs.IsVisible = true;
        for (int i = 0; i < chain.Count; i++)
        {
            HeatNode node = chain[i];
            if (i > 0) _crumbs.Children.Add(new TextBlock { Text = "›", Foreground = AppColors.Brush(AppColors.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0) });
            var crumb = new Button
            {
                Content = node.Name,
                Background = Brushes.Transparent,
                Padding = new Thickness(6, 2),
                FontWeight = i == chain.Count - 1 ? FontWeight.Bold : FontWeight.Normal,
            };
            ToolTip.SetTip(crumb, MapStrings.HeatCrumbTip);
            crumb.Click += (_, _) => _canvas.ZoomTo(node.Path);
            _crumbs.Children.Add(crumb);
        }
    }

    /// <summary>The detail is the one from "Knowledge": it exists only for the files the agent has touched.</summary>
    private void RefreshDetail() =>
        _detail.Update(_canvas.SelectedPath is { } selected && _graph is not null ? NodeDetail.Of(_graph, selected, _projectRoot) : null, _projectRoot);
}
