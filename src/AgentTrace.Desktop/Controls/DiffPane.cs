using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Git;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>"Diff Inspector" tab: list of changed files, baseline and the diff of the selected one.</summary>
public sealed class DiffPane : UserControl
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    private readonly ListBox _list = new();
    private readonly ComboBox _baseline = new() { ItemsSource = new[] { TextStrings.DiffBaselineSession, TextStrings.DiffBaselineHead }, SelectedIndex = 0 };
    private readonly TextBlock _gitStatus = new() { Text = TextStrings.DiffOpenProjectStatus, Foreground = AppColors.Brush(AppColors.Muted) };
    private readonly Button _reveal = new() { Content = TextStrings.RevealInTree, IsEnabled = false };
    private readonly Button _reset = new() { Content = TextStrings.DiffResetBaseline };
    private readonly CodeViewer _viewer = new();
    private FilePath? _project;
    private List<DiffRow>? _shown;
    private bool _syncing;

    public DiffPane()
    {
        _list.ItemTemplate = new FuncDataTemplate<ChangeEntry>((entry, _) => new TextBlock
        {
            Text = entry is null ? "" : ChangeFormatter.Row(_project ?? new FilePath("."), entry),
            FontFamily = Mono,
            FontSize = 12,
        });
        _list.SelectionChanged += (_, _) =>
        {
            _reveal.IsEnabled = _list.SelectedItem is not null;
            if (!_syncing) SelectionChanged?.Invoke(_list.SelectedItem as ChangeEntry);
        };
        _list.DoubleTapped += (_, _) =>
        {
            if (_list.SelectedItem is ChangeEntry entry) OpenRequested?.Invoke(entry.Change.Path);
        };
        _baseline.SelectionChanged += (_, _) =>
        {
            if (!_syncing) BaselineChanged?.Invoke(_baseline.SelectedIndex == 0 ? GitChangeSource.BaselineMode.Session : GitChangeSource.BaselineMode.Head);
        };
        _reset.Click += (_, _) => ResetRequested?.Invoke();
        _reveal.Click += (_, _) =>
        {
            if (_list.SelectedItem is ChangeEntry entry) RevealRequested?.Invoke(entry.Change.Path);
        };

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = TextStrings.DiffCompareLabel, VerticalAlignment = VerticalAlignment.Center }, _baseline, _reset, _reveal },
        };
        var left = new DockPanel();
        var placeholder = new TextBlock { Text = TextStrings.DiffNoChangesPlaceholder, Foreground = AppColors.Brush(AppColors.Muted), Margin = new Thickness(8) };
        left.Children.Add(_list);
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("380,Auto,*") };
        split.Children.Add(left);
        var splitter = new GridSplitter { Width = 4, Background = AppColors.Brush(AppColors.BorderSoft) };
        Grid.SetColumn(splitter, 1);
        split.Children.Add(splitter);
        Grid.SetColumn(_viewer, 2);
        split.Children.Add(_viewer);

        var header = new StackPanel { Spacing = 6, Children = { controls, _gitStatus }, Margin = new Thickness(0, 0, 0, 6) };
        var root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(split);
        Content = root;
        ShowMessage(TextStrings.DiffSelectFile);
        _ = placeholder;
    }

    public event Action<ChangeEntry?>? SelectionChanged;

    public event Action<FilePath>? OpenRequested;

    public event Action<FilePath>? RevealRequested;

    public event Action<GitChangeSource.BaselineMode>? BaselineChanged;

    public event Action? ResetRequested;

    public CodeViewer Viewer => _viewer;

    public IReadOnlyList<ChangeEntry> Items => _list.Items.OfType<ChangeEntry>().ToList();

    public ChangeEntry? Selected => _list.SelectedItem as ChangeEntry;

    public string GitStatus => _gitStatus.Text ?? "";

    public void SetGitStatus(string text) => _gitStatus.Text = text;

    /// <summary>Clears the list and the viewer and goes back to the default baseline (when another project is opened).</summary>
    public void Reset(FilePath project)
    {
        _project = project;
        _syncing = true;
        _list.ItemsSource = Array.Empty<ChangeEntry>();
        _baseline.SelectedIndex = 0;
        _syncing = false;
        _shown = null;
        ShowMessage(TextStrings.DiffSelectFile);
    }

    /// <summary>
    /// Sets the change list keeping the selection; if there was none (or the file is gone) the first one is shown.
    /// <paramref name="preferred"/> is the file that was requested to be shown by argument.
    /// </summary>
    public void SetChanges(IReadOnlyList<ChangeEntry> changes, FilePath? preferred)
    {
        FilePath? selectedPath = Selected?.Change.Path;
        if (preferred is { } wanted && changes.Any(c => c.Change.Path == wanted)) selectedPath = wanted;
        _syncing = true;
        _list.ItemsSource = changes;
        int toSelect = changes.Count == 0 ? -1 : 0;
        for (int i = 0; selectedPath is not null && i < changes.Count; i++)
        {
            if (changes[i].Change.Path == selectedPath)
            {
                toSelect = i;
                break;
            }
        }
        _list.SelectedIndex = toSelect;
        _syncing = false;
        _gitStatus.Text = string.Format(CultureInfo.CurrentCulture, Plural.Pick(changes.Count, TextStrings.DiffFilesChanged_One, TextStrings.DiffFilesChanged_Other), changes.Count);
        SelectionChanged?.Invoke(Selected);
    }

    /// <summary>Selects the file in the list, if it is there.</summary>
    public bool Select(FilePath path)
    {
        List<ChangeEntry> items = [.. Items];
        int index = items.FindIndex(e => e.Change.Path == path);
        if (index < 0) return false;
        _list.SelectedIndex = index;
        _list.ScrollIntoView(index);
        return true;
    }

    /// <summary>Shows the diff only if it changes: this way a refresh without changes does not send the viewer back to the top.</summary>
    public void ShowDiff(IReadOnlyList<DiffRow> rows, string fileName)
    {
        if (_shown is not null && _shown.SequenceEqual(rows)) return;
        _shown = [.. rows];
        _viewer.ShowDiff(rows, fileName);
    }

    public void ShowMessage(string message) => ShowDiff([DiffRow.Message(message)], "");
}
