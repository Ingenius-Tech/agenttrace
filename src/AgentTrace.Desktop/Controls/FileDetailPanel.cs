using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// The side panel with the detail of a file (fields, actions and related files). It is shared by "Knowledge" and "Heatmap".
/// It is rebuilt only if what it shows has changed, so the user's text selection is not lost.
/// </summary>
public sealed class FileDetailPanel : Border
{
    public const double PanelWidth = 320;

    private readonly StackPanel _box = new() { Spacing = 8, Margin = new Thickness(10) };
    private readonly Action<FilePath> _openFile;
    private readonly Action<FilePath> _showDiff;
    private readonly Action<FilePath> _select;
    private readonly Action _close;
    private FilePath? _projectRoot;
    private NodeDetail? _shown;

    /// <param name="openFile">Opens the file in the viewer.</param>
    /// <param name="showDiff">Shows its change in the Inspector.</param>
    /// <param name="select">Selects a related file.</param>
    /// <param name="close">Closes the panel (removes the selection).</param>
    public FileDetailPanel(Action<FilePath> openFile, Action<FilePath> showDiff, Action<FilePath> select, Action close)
    {
        _openFile = openFile;
        _showDiff = showDiff;
        _select = select;
        _close = close;
        Width = PanelWidth;
        Background = AppColors.Brush(AppColors.Background);
        BorderBrush = AppColors.Brush(AppColors.BorderSoft);
        BorderThickness = new Thickness(1, 0, 0, 0);
        Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _box };
        IsVisible = false;
    }

    public bool HasDetail => _shown is not null;

    public IEnumerable<string> Texts => _box.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    /// <summary>Shows the detail (null: hides the panel).</summary>
    public void Update(NodeDetail? detail, FilePath? projectRoot)
    {
        _projectRoot = projectRoot;
        if (Equals(detail, _shown)) return;
        _shown = detail;
        IsVisible = detail is not null;
        if (detail is not null) Show(detail);
    }

    private void Show(NodeDetail d)
    {
        _box.Children.Clear();
        var title = new TextBlock { Text = d.Title, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Foreground = AppColors.Brush(AppColors.Text) };
        var close = new Button { Content = new TextBlock { Text = Codicons.Glyph(Codicon.Close), FontFamily = Codicons.Font }, Padding = new Thickness(4) };
        close.Click += (_, _) => _close();
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(title);
        _box.Children.Add(head);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, RowSpacing = 3 };
        int row = 0;
        foreach (NodeDetail.Field f in d.Fields)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = f.Label, Foreground = AppColors.Brush(AppColors.Muted) };
            var value = new TextBlock { Text = f.Value, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(name, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(name);
            grid.Children.Add(value);
            row++;
        }
        _box.Children.Add(grid);

        Button open = Labelled(Codicon.GoToFile, TextStrings.ActionOpen);
        open.Click += (_, _) => _openFile(d.Path);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { open } };
        if (d.Changed)
        {
            Button diff = Labelled(Codicon.Diff, TextStrings.ActionShowDiff);
            diff.Click += (_, _) => _showDiff(d.Path);
            actions.Children.Add(diff);
        }
        _box.Children.Add(actions);

        if (d.Related.Count == 0) return;
        _box.Children.Add(new TextBlock { Text = TextStrings.FileDetailRelated, Foreground = AppColors.Brush(AppColors.Muted) });
        foreach (NodeDetail.RelatedFile r in d.Related)
        {
            var link = new Button
            {
                Content = NodeDetail.RelativeTo(r.Path, _projectRoot) + "  ×" + r.Weight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
            };
            ToolTip.SetTip(link, r.Relation);
            link.Click += (_, _) => _select(r.Path);
            _box.Children.Add(link);
            _box.Children.Add(new TextBlock { Text = r.Relation, FontSize = 11, Foreground = AppColors.Brush(AppColors.Muted), TextWrapping = TextWrapping.Wrap });
        }
    }

    /// <summary>Icon and text, for the buttons of the tab bars.</summary>
    public static Control LabelledContent(Codicon icon, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children =
        {
            new TextBlock { Text = Codicons.Glyph(icon), FontFamily = Codicons.Font, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    public static Button Labelled(Codicon icon, string text) => new() { Content = LabelledContent(icon, text) };
}
