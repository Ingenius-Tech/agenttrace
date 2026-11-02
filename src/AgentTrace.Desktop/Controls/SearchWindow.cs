using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Core.Search;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Search;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Search of the active session: a non-modal window (Ctrl+K) with the box, the filters and the result list. The index and the
/// query are pure logic (<c>Core.Search</c>); here results are only requested and painted. Enter or double click leads to the result.
/// </summary>
public sealed class SearchWindow : Window
{
    public const int Limit = 500;

    private static readonly (string Label, SearchKind? Kind)[] KindChoices =
    [
        (TextStrings.SearchFilterAll, null), (TextStrings.SearchFilterRequests, SearchKind.Request), (TextStrings.SearchFilterResponses, SearchKind.Response),
        (TextStrings.SearchFilterThinking, SearchKind.Thinking), (TextStrings.SearchFilterTools, SearchKind.Tool), (TextStrings.SearchFilterFiles, SearchKind.File),
        (TextStrings.SearchFilterErrors, SearchKind.Error),
    ];

    private readonly Func<SearchQuery, int, IReadOnlyList<SearchHit>> _search;
    private readonly Action<SearchHit> _jump;
    private readonly TextBox _text = new() { PlaceholderText = TextStrings.SearchPlaceholder, MinWidth = 320 };
    private readonly ComboBox _kind = new() { ItemsSource = KindChoices.Select(k => k.Label).ToList(), SelectedIndex = 0 };
    private readonly ComboBox _lane = new() { ItemsSource = new[] { TextStrings.SearchLaneAll, TextStrings.SearchLaneMainFilter, TextStrings.SearchLaneSubagents }, SelectedIndex = 0 };
    private readonly TextBox _turn = new() { PlaceholderText = TextStrings.SearchTurnPlaceholder, Width = 80 };
    private readonly TextBlock _summary = new() { Foreground = AppColors.Brush(AppColors.Muted), VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox _results = new() { Background = Brushes.Transparent };
    private IReadOnlyList<SearchHit> _hits = [];

    /// <param name="search">Query to the index (query and result limit).</param>
    /// <param name="jump">Leads to the result: the graph step, the turn or the file.</param>
    public SearchWindow(Func<SearchQuery, int, IReadOnlyList<SearchHit>> search, Action<SearchHit> jump)
    {
        _search = search;
        _jump = jump;
        Title = TextStrings.SearchWindowTitle;
        Width = 860;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppColors.Brush(AppColors.Card);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _kind, _lane, _turn } };
        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(filters, Dock.Right);
        head.Children.Add(filters);
        head.Children.Add(_text);
        _text.Margin = new Thickness(0, 0, 8, 0);

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        _summary.Margin = new Thickness(0, 8);
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);
        root.Children.Add(_results);
        Content = root;

        _text.TextChanged += (_, _) => Refresh();
        _turn.TextChanged += (_, _) => Refresh();
        _kind.SelectionChanged += (_, _) => Refresh();
        _lane.SelectionChanged += (_, _) => Refresh();
        _results.DoubleTapped += (_, _) => JumpToSelected();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                JumpToSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && _text.IsFocused && _results.ItemCount > 0)
            {
                _results.SelectedIndex = Math.Max(0, _results.SelectedIndex);
                _results.Focus();
                e.Handled = true;
            }
        };
        Opened += (_, _) => _text.Focus();
    }

    public string QueryText
    {
        get => _text.Text ?? "";
        set
        {
            _text.Text = value;
            Refresh(); // does not wait for the box notification: this way the result is the same with or without the window in view
        }
    }

    public IReadOnlyList<SearchHit> Hits => _hits;

    public string Summary => _summary.Text ?? "";

    /// <summary>Repeats the query with what is in the index (called when a filter changes and when new events arrive).</summary>
    public void Refresh()
    {
        SearchHit? selected = _results.SelectedItem is ListBoxItem { Tag: SearchHit s } ? s : null;
        _hits = _search(CurrentQuery(), Limit);
        _results.ItemsSource = _hits.Select(Row).ToList();
        if (selected is not null)
        {
            int i = _hits.ToList().FindIndex(h => h.Entry == selected.Entry);
            if (i >= 0) _results.SelectedIndex = i;
        }
        _summary.Text = QueryText.Trim().Length == 0 ? TextStrings.SearchHint : SearchPresentation.Summary(_hits.Count, Limit);
    }

    public void JumpToSelected()
    {
        if (_results.SelectedItem is ListBoxItem { Tag: SearchHit hit }) _jump(hit);
    }

    private SearchQuery CurrentQuery()
    {
        SearchKind? kind = KindChoices[Math.Max(0, _kind.SelectedIndex)].Kind;
        SearchLane lane = _lane.SelectedIndex switch { 1 => SearchLane.Main, 2 => SearchLane.Subagent, _ => SearchLane.Any };
        int? turn = int.TryParse(_turn.Text?.Trim(), out int t) && t >= 1 ? t - 1 : null;
        return new SearchQuery(QueryText, kind is { } k ? new HashSet<SearchKind> { k } : null, lane, turn);
    }

    private static ListBoxItem Row(SearchHit hit)
    {
        SearchEntry entry = hit.Entry;
        Color color = entry.Kind switch { SearchKind.Error => AppColors.Deleted, SearchKind.File => AppColors.Read, _ => AppColors.Muted };
        var header = new TextBlock { FontSize = 11, Foreground = AppColors.Brush(AppColors.Muted) };
        header.Inlines!.Add(new Run(SearchPresentation.KindLabel(entry.Kind)) { Foreground = AppColors.Brush(color), FontWeight = FontWeight.SemiBold });
        header.Inlines.Add(new Run("  " + SearchPresentation.Where(entry)));

        var snippet = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        int at = 0;
        foreach ((int start, int length) in hit.Highlights)
        {
            if (start < at) continue; // overlapping marks (two words that step on each other)
            if (start > at) snippet.Inlines!.Add(new Run(hit.Snippet[at..start]));
            snippet.Inlines!.Add(new Run(hit.Snippet.Substring(start, length)) { FontWeight = FontWeight.Bold, Foreground = AppColors.Brush(AppColors.Modified) });
            at = start + length;
        }
        if (at < hit.Snippet.Length) snippet.Inlines!.Add(new Run(hit.Snippet[at..]));

        return new ListBoxItem { Tag = hit, Content = new StackPanel { Spacing = 1, Children = { header, snippet } } };
    }
}
