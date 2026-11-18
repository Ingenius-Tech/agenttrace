using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// The Areas map tab: filters by kind of activity, the dimension the rows show, the canvas and, when a cell is selected, a panel with the
/// files of that area in that turn (double click or Enter opens the file, a button shows its diff).
/// </summary>
public sealed class AreasPane : UserControl
{
    private const int MaxErrorLines = 5;

    private static readonly AreaDimension[] Dimensions = [AreaDimension.Layer, AreaDimension.Module, AreaDimension.Kind];

    private readonly AreasCanvasView _canvas;
    private readonly Action<FilePath> _openFile;
    private readonly Action<FilePath> _showDiff;
    private readonly ToggleButton _reads = new() { Content = FileDetailPanel.LabelledContent(Codicon.Eye, MapStrings.HeatReads), IsChecked = true };
    private readonly ToggleButton _writes = new() { Content = FileDetailPanel.LabelledContent(Codicon.Diff, MapStrings.HeatWrites), IsChecked = true };
    private readonly ToggleButton _errors = new() { Content = FileDetailPanel.LabelledContent(Codicon.Close, MapStrings.HeatErrors), IsChecked = true };
    private readonly ComboBox _group = new() { ItemsSource = Dimensions.Select(AreaText.Group).ToArray(), SelectedIndex = 1 };
    private readonly TextBlock _source = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = AppColors.Brush(AppColors.Muted) };
    private readonly TextBlock _coverage = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = AppColors.Brush(AppColors.Muted) };
    private readonly TextBlock _truncated = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Text = AreaStrings.CoverageTruncated, Foreground = AppColors.Brush(AppColors.Muted), IsVisible = false };
    private readonly Button _copyPrompt = new() { Content = FileDetailPanel.LabelledContent(Codicon.Files, AreaStrings.CopyPromptButton), IsVisible = false };
    private readonly TextBlock _copied = new() { VerticalAlignment = VerticalAlignment.Center, Text = AreaStrings.CopiedNote, Foreground = AppColors.Brush(AppColors.Muted), IsVisible = false };
    private readonly StackPanel _errorLines = new() { Margin = new Thickness(8, 0, 8, 4), IsVisible = false };
    private readonly Border _panel = new() { Width = FileDetailPanel.PanelWidth, IsVisible = false };
    private readonly StackPanel _panelBox = new() { Spacing = 8, Margin = new Thickness(10) };
    private AreasSnapshot _snapshot = AreasSnapshot.Empty;
    private AreaDimension _dimension = AreaDimension.Module;
    private bool _dimensionChosen;
    private bool _settingGroup;
    private (AreaRowKey Row, int Turn, AreaCell Cell)? _panelShown;
    private string? _pendingSelect;
    private string? _pendingExpand;

    /// <param name="openFile">Opens a file in the viewer.</param>
    /// <param name="showDiff">Shows its change in the Inspector.</param>
    public AreasPane(Action<FilePath> openFile, Action<FilePath> showDiff)
    {
        _openFile = openFile;
        _showDiff = showDiff;
        _canvas = new AreasCanvasView(openFile);
        CopyText = text => TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : Task.CompletedTask;

        ToolTip.SetTip(_reads, MapStrings.HeatReadsTip);
        ToolTip.SetTip(_writes, MapStrings.HeatWritesTip);
        ToolTip.SetTip(_errors, MapStrings.HeatErrorsTip);
        ToolTip.SetTip(_group, AreaStrings.GroupTip);
        foreach (ToggleButton button in new[] { _reads, _writes, _errors }) button.Click += (_, e) => OnFilterClicked((ToggleButton)e.Source!);
        ToolTip.SetTip(_coverage, AreaStrings.CoverageTip);
        _copyPrompt.Click += async (_, _) => await CopySetupPrompt();
        _group.SelectionChanged += (_, _) =>
        {
            if (_settingGroup) return;
            _dimension = Dimensions[Math.Max(0, _group.SelectedIndex)];
            _dimensionChosen = true;
            Rebuild();
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 4),
            Children = { _reads, _writes, _errors, new TextBlock { Text = AreaStrings.GroupLabel, Foreground = AppColors.Brush(AppColors.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }, _group },
        };
        _panel.Background = AppColors.Brush(AppColors.Background);
        _panel.BorderBrush = AppColors.Brush(AppColors.BorderSoft);
        _panel.BorderThickness = new Thickness(1, 0, 0, 0);
        _panel.Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _panelBox };

        // Wraps onto further lines when the items do not fit, so nothing is clipped at the right edge
        var strip = new WrapPanel
        {
            ItemSpacing = 12,
            LineSpacing = 2,
            Margin = new Thickness(8, 0, 8, 4),
            Children = { _source, _coverage, _truncated, _copyPrompt, _copied },
        };
        var top = new StackPanel { Children = { bar, strip, _errorLines } };

        var content = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        content.Children.Add(top);
        DockPanel.SetDock(_panel, Dock.Right);
        content.Children.Add(_panel);
        content.Children.Add(_canvas);
        Content = content;

        _canvas.CellSelectionChanged += RefreshPanel;
    }

    public AreasCanvasView Canvas => _canvas;

    /// <summary>Writes text to the clipboard; replaceable so a test can observe what is copied (the default uses the window's clipboard).</summary>
    public Func<string, Task> CopyText { get; set; } = null!;

    /// <summary>The "Copy setup prompt" button, visible only while some repository has no valid rules file.</summary>
    public Button CopyPromptButton => _copyPrompt;

    public string SourceText => _source.Text ?? "";

    public string CoverageText => _coverage.Text ?? "";

    public bool CoverageTruncated => _truncated.IsVisible;

    public bool CopiedNoteVisible => _copied.IsVisible;

    public IEnumerable<string> ErrorTexts => _errorLines.Children.OfType<TextBlock>().Select(t => t.Text ?? "");

    public AreaDimension Dimension => _dimension;

    public ActivityFilter Filter => new(_reads.IsChecked == true, _writes.IsChecked == true, _errors.IsChecked == true);

    public bool PanelVisible => _panel.IsVisible;

    public IEnumerable<string> PanelTexts => _panelBox.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    /// <summary>The rows of the panel (one per file), in order, to open one from a test.</summary>
    public IReadOnlyList<Control> PanelFileRows => [.. _panelBox.Children.OfType<Grid>().Where(g => g.Tag is FilePath)];

    /// <summary>Updates the map with the data of the session (called often while live).</summary>
    public void SetSnapshot(AreasSnapshot snapshot)
    {
        _snapshot = snapshot;
        RefreshStrip();
        if (!_dimensionChosen) SetDimension(snapshot.Repos.DefaultDimension, chosen: false);
        Rebuild();
    }

    /// <summary>Empties the map (when opening another project or switching session).</summary>
    public void Clear() => SetSnapshot(AreasSnapshot.Empty);

    /// <summary>The dimension of the rows (<c>--areas-group</c> too).</summary>
    public void SetDimension(AreaDimension dimension, bool chosen = true)
    {
        _dimension = dimension;
        _dimensionChosen |= chosen;
        _settingGroup = true;
        _group.SelectedIndex = Array.IndexOf(Dimensions, dimension);
        _settingGroup = false;
        if (chosen) Rebuild();
    }

    /// <summary>The chosen kinds of activity (those of <c>--areas-filter</c> too).</summary>
    public void SetFilter(ActivityFilter filter)
    {
        _reads.IsChecked = filter.Reads;
        _writes.IsChecked = filter.Writes;
        _errors.IsChecked = filter.Errors;
        Rebuild();
    }

    /// <summary>
    /// <c>--areas-select</c> (<c>task:&lt;n&gt;</c>, <c>turn:&lt;n&gt;</c> or an area id) and <c>--areas-expand</c> (an area id): applied as soon as they exist.
    /// </summary>
    public void SetPending(string? select, string? expand)
    {
        _pendingSelect = select;
        _pendingExpand = expand;
        ApplyPending();
    }

    /// <summary>The source of the rules, their coverage, the errors of an invalid file and the button that offers the setup prompt.</summary>
    private void RefreshStrip()
    {
        AreaRepoSet repos = _snapshot.Repos;
        _source.Text = AreaText.Source(repos);
        _coverage.IsVisible = _snapshot.Coverage.Dimensions.Count > 0;
        _coverage.Text = _coverage.IsVisible ? string.Format(CultureInfo.CurrentCulture, AreaStrings.CoverageLineFormat, AreaText.CoverageItems(_snapshot.Coverage)) : "";
        _truncated.IsVisible = _snapshot.Coverage.Truncated;
        _copyPrompt.IsVisible = repos.NeedsRules;
        if (!repos.NeedsRules) _copied.IsVisible = false;
        _errorLines.Children.Clear();
        foreach (string line in AreaText.ErrorLines(repos, MaxErrorLines))
            _errorLines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Foreground = AppColors.Brush(AppColors.Error) });
        _errorLines.IsVisible = _errorLines.Children.Count > 0;
    }

    /// <summary>The only place the clipboard is written: the user's click.</summary>
    private async Task CopySetupPrompt()
    {
        await CopyText(AreaStrings.SetupPrompt);
        _copied.IsVisible = true;
    }

    private void OnFilterClicked(ToggleButton clicked)
    {
        // At least one kind always stays active
        if (_reads.IsChecked != true && _writes.IsChecked != true && _errors.IsChecked != true) clicked.IsChecked = true;
        Rebuild();
    }

    private void Rebuild()
    {
        _canvas.SetMatrix(AreasMatrix.Build(_snapshot, _dimension, Filter));
        ApplyPending();
        RefreshPanel();
    }

    private void ApplyPending()
    {
        AreasMatrix matrix = _canvas.Matrix;
        if (_pendingExpand is { } expand && matrix.RowOfArea(expand) is var row && row >= 0)
        {
            _canvas.Expand(matrix.Rows[row].Key);
            _pendingExpand = null;
        }
        if (_pendingSelect is not { } select) return;
        if (select.StartsWith("task:", StringComparison.Ordinal))
        {
            if (!int.TryParse(select[5..], out int task)) _pendingSelect = null;
            else if (task < _snapshot.Tasks.Count)
            {
                _canvas.SelectTask(task);
                _pendingSelect = null;
            }
        }
        else if (select.StartsWith("turn:", StringComparison.Ordinal))
        {
            if (!int.TryParse(select[5..], out int turn)) _pendingSelect = null;
            else if (matrix.ColumnOfTurn(turn) >= 0)
            {
                _canvas.SelectTurn(turn);
                _pendingSelect = null;
            }
        }
        else if (matrix.RowOfArea(select) is var area && area >= 0)
        {
            _canvas.Select(area);
            _pendingSelect = null;
        }
    }

    // ---- the panel of a cell ----

    private void RefreshPanel()
    {
        if (_canvas.SelectedCell is not { } selected || _canvas.Matrix.Cell(selected.Row, selected.Column) is not { } cell)
        {
            _panelShown = null;
            _panel.IsVisible = false;
            return;
        }
        AreasMatrix matrix = _canvas.Matrix;
        int turn = matrix.Columns[selected.Column].Turn;
        var shown = (matrix.Rows[selected.Row].Key, turn, cell);
        _panel.IsVisible = true;
        if (_panelShown is { } before && before.Equals(shown)) return;
        _panelShown = shown;
        ShowPanel(matrix.Rows[selected.Row].Name, turn, cell);
    }

    private void ShowPanel(string areaName, int turn, AreaCell cell)
    {
        _panelBox.Children.Clear();
        var title = new TextBlock { Text = AreaText.CellTitle(areaName, turn), FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Foreground = AppColors.Brush(AppColors.Text) };
        var close = new Button { Content = new TextBlock { Text = Codicons.Glyph(Codicon.Close), FontFamily = Codicons.Font }, Padding = new Thickness(4) };
        close.Click += (_, _) => _canvas.Select(null);
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(title);
        _panelBox.Children.Add(head);
        _panelBox.Children.Add(new TextBlock { Text = AreaText.Counts(cell.Activity, _canvas.Matrix.Filter), Foreground = AppColors.Brush(AppColors.Muted), TextWrapping = TextWrapping.Wrap });
        foreach (AreaFileCell file in cell.Files) _panelBox.Children.Add(FileRow(file));
    }

    private Control FileRow(AreaFileCell file)
    {
        string name = NodeDetail.RelativeTo(file.Path, _snapshot.Project);
        var label = new StackPanel { Children = { new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis }, new TextBlock { Text = AreaText.Counts(file.Activity, _canvas.Matrix.Filter), FontSize = 11, Foreground = AppColors.Brush(AppColors.Muted) } } };
        var open = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
        ToolTip.SetTip(open, file.Path.Value);
        open.DoubleTapped += (_, _) => _openFile(file.Path);
        open.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            _openFile(file.Path);
            e.Handled = true;
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Tag = file.Path };
        row.Children.Add(open);
        if (file.Activity.Writes > 0)
        {
            Button diff = FileDetailPanel.Labelled(Codicon.Diff, TextStrings.ActionShowDiff);
            diff.Click += (_, _) => _showDiff(file.Path);
            Grid.SetColumn(diff, 1);
            row.Children.Add(diff);
        }
        return row;
    }

    /// <summary>Opens the file of a row of the panel as a double click would (for tests).</summary>
    public void OpenPanelRow(int index) => _openFile((FilePath)PanelFileRows[index].Tag!);
}
