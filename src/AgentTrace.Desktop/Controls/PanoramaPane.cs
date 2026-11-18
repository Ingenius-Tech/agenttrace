using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// The Panorama tab: the Execution graph on the left, the Areas map over Knowledge on the right (an "L" layout), with two splitters and one
/// show/hide toggle per pane. The panes are the same instances as in their own tabs: while the Panorama is shown it takes them out of their
/// <see cref="PaneSlot"/>s (<see cref="Attach"/>) and gives them back when another tab is selected (<see cref="Detach"/>).
/// </summary>
public sealed class PanoramaPane : UserControl
{
    public const double GraphMinWidth = 200;
    public const double RightMinWidth = 280;
    public const double RowMinHeight = 120;

    private readonly PaneSlot _graph;
    private readonly PaneSlot _areas;
    private readonly PaneSlot _knowledge;
    private readonly Grid _outer = new() { ColumnDefinitions = new ColumnDefinitions("38*,Auto,62*") };
    private readonly Grid _right = new() { RowDefinitions = new RowDefinitions("40*,Auto,60*") };
    private readonly GridSplitter _columnSplitter = Splitter(GridResizeDirection.Columns);
    private readonly GridSplitter _rowSplitter = Splitter(GridResizeDirection.Rows);
    private readonly ToggleButton _graphToggle;
    private readonly ToggleButton _areasToggle;
    private readonly ToggleButton _knowledgeToggle;
    private readonly Button _clearFocus = new() { Content = ShellStrings.PanoramaClearFocus, IsVisible = false };
    private GridLength _graphWidth = new(38, GridUnitType.Star);
    private GridLength _rightWidth = new(62, GridUnitType.Star);
    private GridLength _areasHeight = new(40, GridUnitType.Star);
    private GridLength _knowledgeHeight = new(60, GridUnitType.Star);

    /// <summary>Raised when the user shows or hides a pane (the window refreshes the pane that appears).</summary>
    public event Action? VisibilityChanged;

    /// <summary>The user asked to remove the highlights that link the panes.</summary>
    public event Action? ClearFocusRequested;

    public PanoramaPane(PaneSlot graph, PaneSlot areas, PaneSlot knowledge)
    {
        _graph = graph;
        _areas = areas;
        _knowledge = knowledge;
        _graphToggle = Toggle(ShellStrings.TabExecutionGraph);
        _areasToggle = Toggle(ShellStrings.TabAreas);
        _knowledgeToggle = Toggle(ShellStrings.TabKnowledge);
        ToolTip.SetTip(_clearFocus, ShellStrings.PanoramaClearFocusTip);
        _clearFocus.Click += (_, _) => ClearFocusRequested?.Invoke();

        Grid.SetRow(_rowSplitter, 1);
        _right.Children.Add(_rowSplitter);
        Grid.SetColumn(_columnSplitter, 1);
        Grid.SetColumn(_right, 2);
        _outer.Children.Add(_columnSplitter);
        _outer.Children.Add(_right);

        var bar = new WrapPanel
        {
            ItemSpacing = 6,
            LineSpacing = 4,
            Margin = new Thickness(8, 4),
            Children = { _graphToggle, _areasToggle, _knowledgeToggle, _clearFocus },
        };
        var content = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        content.Children.Add(bar);
        content.Children.Add(_outer);
        Content = content;
        ApplyVisibility();
    }

    /// <summary>True while the panes are hosted here (the Panorama tab is selected).</summary>
    public bool IsAttached { get; private set; }

    public bool GraphVisible => _graphToggle.IsChecked == true;

    public bool AreasVisible => _areasToggle.IsChecked == true;

    public bool KnowledgeVisible => _knowledgeToggle.IsChecked == true;

    /// <summary>The "clear link" button, visible only while the panes highlight a focus.</summary>
    public Button ClearFocusButton => _clearFocus;

    public bool FocusActive
    {
        get => _clearFocus.IsVisible;
        set => _clearFocus.IsVisible = value;
    }

    public ToggleButton GraphToggle => _graphToggle;

    public ToggleButton AreasToggle => _areasToggle;

    public ToggleButton KnowledgeToggle => _knowledgeToggle;

    public GridSplitter ColumnSplitter => _columnSplitter;

    public GridSplitter RowSplitter => _rowSplitter;

    /// <summary>The graph column, the right column, the Areas row and the Knowledge row (their widths and heights, for tests).</summary>
    public (GridLength Graph, GridLength Right, GridLength Areas, GridLength Knowledge) Sizes =>
        (_outer.ColumnDefinitions[0].Width, _outer.ColumnDefinitions[2].Width, _right.RowDefinitions[0].Height, _right.RowDefinitions[2].Height);

    /// <summary>Takes the three panes out of their tabs and places them in the layout. Does nothing if already attached.</summary>
    public void Attach()
    {
        if (IsAttached) return;
        IsAttached = true;
        _graph.Take();
        _areas.Take();
        _knowledge.Take();
        Place(_graph.Pane, _outer, column: 0, row: 0);
        Place(_areas.Pane, _right, column: 0, row: 0);
        Place(_knowledge.Pane, _right, column: 0, row: 2);
        ApplyVisibility();
    }

    /// <summary>Gives the panes back to their tabs. Does nothing if not attached.</summary>
    public void Detach()
    {
        if (!IsAttached) return;
        IsAttached = false;
        _outer.Children.Remove(_graph.Pane);
        _right.Children.Remove(_areas.Pane);
        _right.Children.Remove(_knowledge.Pane);
        _graph.Pane.IsVisible = true;
        _areas.Pane.IsVisible = true;
        _knowledge.Pane.IsVisible = true;
        _graph.Return();
        _areas.Return();
        _knowledge.Return();
    }

    private static void Place(Control pane, Grid grid, int column, int row)
    {
        Grid.SetColumn(pane, column);
        Grid.SetRow(pane, row);
        grid.Children.Add(pane);
    }

    private ToggleButton Toggle(string name)
    {
        var button = new ToggleButton { Content = name, IsChecked = true };
        ToolTip.SetTip(button, string.Format(CultureInfo.CurrentCulture, ShellStrings.PanoramaToggleTipFormat, name));
        button.Click += (_, _) =>
        {
            if (!GraphVisible && !AreasVisible && !KnowledgeVisible) button.IsChecked = true; // at least one pane stays
            ApplyVisibility();
            VisibilityChanged?.Invoke();
        };
        return button;
    }

    /// <summary>Collapses the hidden panes' columns and rows (remembering their sizes) so the visible ones take the space.</summary>
    private void ApplyVisibility()
    {
        bool graph = GraphVisible, areas = AreasVisible, knowledge = KnowledgeVisible, right = areas || knowledge;
        if (IsAttached)
        {
            _graph.Pane.IsVisible = graph;
            _areas.Pane.IsVisible = areas;
            _knowledge.Pane.IsVisible = knowledge;
        }
        _columnSplitter.IsVisible = graph && right;
        _rowSplitter.IsVisible = areas && knowledge;
        ResizeColumn(_outer.ColumnDefinitions[0], ref _graphWidth, graph, GraphMinWidth);
        ResizeColumn(_outer.ColumnDefinitions[2], ref _rightWidth, right, RightMinWidth);
        ResizeRow(_right.RowDefinitions[0], ref _areasHeight, areas);
        ResizeRow(_right.RowDefinitions[2], ref _knowledgeHeight, knowledge);
    }

    // A hidden definition has zero size; its last size (what the splitter left) is kept to restore it
    private static void ResizeColumn(ColumnDefinition column, ref GridLength saved, bool shown, double minimum)
    {
        if (column.Width.Value > 0) saved = column.Width;
        column.MinWidth = shown ? minimum : 0;
        column.Width = shown ? saved : new GridLength(0);
    }

    private static void ResizeRow(RowDefinition row, ref GridLength saved, bool shown)
    {
        if (row.Height.Value > 0) saved = row.Height;
        row.MinHeight = shown ? RowMinHeight : 0;
        row.Height = shown ? saved : new GridLength(0);
    }

    private static GridSplitter Splitter(GridResizeDirection direction) => new()
    {
        Background = AppColors.Brush(AppColors.BorderSoft),
        ResizeDirection = direction,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        Width = direction == GridResizeDirection.Columns ? 4 : double.NaN,
        Height = direction == GridResizeDirection.Rows ? 4 : double.NaN,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
}
