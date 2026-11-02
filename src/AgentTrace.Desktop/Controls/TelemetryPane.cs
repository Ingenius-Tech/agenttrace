using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>"Process Telemetry" tab: the agent's events, newest on top. Double-clicking a file event opens it.</summary>
public sealed class TelemetryPane : UserControl
{
    public const int MaxItems = 5000;

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    private readonly ObservableCollection<TelemetryRow> _items = [];
    private readonly ListBox _list = new();

    public TelemetryPane(Action<FilePath> onOpen)
    {
        _list.ItemsSource = _items;
        _list.ItemTemplate = new FuncDataTemplate<TelemetryRow>((row, _) => new TextBlock { Text = row?.Text ?? "", FontFamily = Mono, FontSize = 12 });
        _list.DoubleTapped += (_, _) =>
        {
            if (_list.SelectedItem is TelemetryRow { Path: { } path }) onOpen(path);
        };
        var header = new TextBlock { Text = TextStrings.TelemetryHeader, FontWeight = FontWeight.Bold, Foreground = AppColors.Brush(AppColors.Text), Margin = new Thickness(0, 0, 0, 8) };
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_list);
        Content = root;
        Clear();
    }

    public IReadOnlyList<TelemetryRow> Items => _items;

    public void Clear()
    {
        _items.Clear();
        _items.Add(TelemetryRow.Plain(TextStrings.TelemetryEmpty));
    }

    /// <summary>Adds the events of a burst (<paramref name="lines"/> in chronological order): the newest ends up on top.</summary>
    public void AddAtTop(IReadOnlyList<TelemetryRow> lines)
    {
        if (lines.Count == 0) return;
        if (_items.Count == 1 && _items[0].Path is null && _items[0].Text == TextStrings.TelemetryEmpty) _items.Clear();
        for (int i = 0; i < lines.Count; i++) _items.Insert(0, lines[i]);
        while (_items.Count > MaxItems) _items.RemoveAt(_items.Count - 1);
    }
}
