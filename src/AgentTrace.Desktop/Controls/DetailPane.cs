using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Graph;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Desktop.Controls;

/// <summary>Side panel with the detail of the selected step or turn. It only presents what <see cref="StepDetail"/> computes.</summary>
public sealed class DetailPane : Border
{
    public const double PaneWidth = 340;

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    private readonly Action<FilePath> _open;
    private readonly Action<FilePath> _diff;
    private readonly Action<string> _gotoLane;
    private readonly Action _close;
    private readonly StackPanel _content = new() { Spacing = 8, Margin = new Thickness(10) };

    public DetailPane(Action<FilePath> open, Action<FilePath> diff, Action<string> gotoLane, Action close)
    {
        _open = open;
        _diff = diff;
        _gotoLane = gotoLane;
        _close = close;
        Width = PaneWidth;
        Background = AppColors.Brush(AppColors.Background);
        BorderBrush = AppColors.Brush(AppColors.BorderSoft);
        BorderThickness = new Thickness(1, 0, 0, 0);
        Child = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _content };
    }

    /// <summary>The texts shown (for tests).</summary>
    public IEnumerable<string> Texts => _content.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    public void Show(StepDetail d)
    {
        _content.Children.Clear();
        var title = new TextBlock { Text = d.Title, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Foreground = AppColors.Brush(AppColors.Text) };
        var close = new Button { Content = new TextBlock { Text = Codicons.Glyph(Codicon.Close), FontFamily = Codicons.Font }, Padding = new Thickness(4) };
        ToolTip.SetTip(close, TextStrings.DetailCloseTooltip);
        close.Click += (_, _) => _close();
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(title);
        _content.Children.Add(head);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, RowSpacing = 3 };
        int row = 0;
        foreach (StepDetail.Field f in d.Fields)
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
        _content.Children.Add(grid);

        if (d.SubagentId is { } subagent)
        {
            var go = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = Codicons.Glyph(Codicon.RepoForked), FontFamily = Codicons.Font },
                        new TextBlock { Text = TextStrings.DetailGoToSubagent },
                    },
                },
            };
            go.Click += (_, _) => _gotoLane(subagent);
            _content.Children.Add(go);
        }
        foreach (string note in d.Notes) _content.Children.Add(TextArea("", note, 4));
        if (d.Files.Count > 0)
        {
            _content.Children.Add(new TextBlock { Text = TextStrings.DetailFilesHeading, Foreground = AppColors.Brush(AppColors.Muted) });
            foreach (FileTouch f in d.Files) _content.Children.Add(FileRow(f, d.ViaGit.Contains(f.Path)));
        }
        if (!string.IsNullOrWhiteSpace(d.Input)) _content.Children.Add(TextArea(d.InputLabel, d.Input, 6));
        if (!string.IsNullOrWhiteSpace(d.Output)) _content.Children.Add(TextArea(d.OutputLabel, d.Output, 10));
    }

    private static Control TextArea(string label, string value, int rows)
    {
        var box = new StackPanel { Spacing = 3 };
        if (label.Length > 0) box.Children.Add(new TextBlock { Text = label, Foreground = AppColors.Brush(AppColors.Muted) });
        box.Children.Add(new TextBox
        {
            Text = value,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            FontFamily = Mono,
            FontSize = 11,
            MaxHeight = rows * 18 + 12,
        });
        return box;
    }

    private Control FileRow(FileTouch f, bool viaGit)
    {
        string action = f.Kind is { } kind ? ChangeStyle.Label(kind) : TextStrings.DetailActionRead;
        var name = new TextBlock
        {
            Text = action + " · " + f.Path.FileName + (viaGit ? " (git)" : ""),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(name, f.Path + (viaGit ? "\n" + TextStrings.DetailViaGitTooltip : ""));
        var open = new Button { Content = TextStrings.ActionOpen };
        open.Click += (_, _) => _open(f.Path);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { open } };
        if (f.Kind is not null)
        {
            var diff = new Button { Content = TextStrings.ActionShowDiff };
            diff.Click += (_, _) => _diff(f.Path);
            buttons.Children.Add(diff);
        }
        var row = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(name);
        return row;
    }
}
