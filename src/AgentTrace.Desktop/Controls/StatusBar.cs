using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// The bottom bar. Following the guidelines of the VS Code one: on the left the general part (the status message: project,
/// agent and session) and on the right the contextual part (the session counters). Each counter carries an icon, a bold number
/// in the color of its tone and a short label.
/// </summary>
public sealed class StatusBar : Border
{
    private readonly TextBlock _message = new()
    {
        FontSize = 13,
        FontWeight = FontWeight.Bold,
        Foreground = AppColors.Brush(AppColors.Text),
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StackPanel _counters = new() { Orientation = Orientation.Horizontal, Spacing = 18 };

    public StatusBar()
    {
        Background = AppColors.Brush(AppColors.Card);
        BorderBrush = AppColors.Brush(AppColors.BorderSoft);
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(14, 7);
        var info = new TextBlock
        {
            Text = Codicons.Glyph(Codicon.Info),
            FontFamily = Codicons.Font,
            FontSize = 14,
            Foreground = AppColors.Brush(AppColors.Read),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The message takes what is left and is cut with "…"; the counters never shrink
        var left = new DockPanel { Margin = new Thickness(0, 0, 24, 0) };
        DockPanel.SetDock(info, Dock.Left);
        _message.Margin = new Thickness(8, 0, 0, 0);
        left.Children.Add(info);
        left.Children.Add(_message);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(_counters, 1);
        grid.Children.Add(_counters);
        Child = grid;
    }

    public string Message
    {
        get => _message.Text ?? "";
        set => _message.Text = value;
    }

    public void SetStats(IReadOnlyList<StatusSegments.Segment> segments)
    {
        _counters.Children.Clear();
        foreach (StatusSegments.Segment s in segments)
        {
            IBrush tone = AppColors.Brush(ToneColor(s.Tone));
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(new TextBlock
            {
                Text = Codicons.Glyph(StatusIconNames.Of(s.Kind)),
                FontFamily = Codicons.Font,
                FontSize = 14,
                Foreground = tone,
                VerticalAlignment = VerticalAlignment.Center,
            });
            item.Children.Add(new TextBlock { Text = s.Value, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = tone, VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = s.Label, FontSize = 12, Foreground = AppColors.Brush(AppColors.Muted), VerticalAlignment = VerticalAlignment.Center });
            ToolTip.SetTip(item, s.Tooltip);
            _counters.Children.Add(item);
        }
    }

    private static Color ToneColor(StatusSegments.Tone tone) => tone switch
    {
        StatusSegments.Tone.Neutral => AppColors.Text,
        StatusSegments.Tone.Info => AppColors.Read,
        StatusSegments.Tone.Ok => AppColors.Created,
        StatusSegments.Tone.Warn => AppColors.Modified,
        StatusSegments.Tone.Error => AppColors.Deleted,
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}
