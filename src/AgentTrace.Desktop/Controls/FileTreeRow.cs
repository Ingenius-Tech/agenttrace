using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Text;
using AgentTrace.Presentation.Tree;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Tree row: icon, name and status badge. It only translates to styles what <see cref="NodeDecoration"/> decides.
/// Color is never the only signal: the badge carries a symbol, external entries are italic and outlined, and deleted ones struck through.
/// </summary>
public sealed class FileTreeRow : ContentControl
{
    private static readonly FontFamily Mono = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    private readonly FileTreeNode _node;
    private readonly TreeContext _context;

    public FileTreeRow(FileTreeNode node, TreeContext context)
    {
        _node = node;
        _context = context;
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _node.Changed += Rebuild;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _node.Changed -= Rebuild;
        base.OnDetachedFromVisualTree(e);
    }

    private void Rebuild()
    {
        if (_node.IsPlaceholder)
        {
            Content = new TextBlock { Text = "…", Foreground = AppColors.Brush(AppColors.Muted) };
            return;
        }
        FileNode node = _node.Node;
        NodeDecoration decoration = NodeDecoration.Of(node, _context.Index);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock
        {
            Text = Codicons.Glyph(FileIconNames.Of(node, _node.IsExpanded, _node.IsRoot)),
            FontFamily = Codicons.Font,
            FontSize = 15,
            Foreground = AppColors.Brush(AppColors.Text),
            Opacity = node.Ghost ? 0.5 : 1,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock
        {
            Text = node.Name,
            Foreground = AppColors.Brush(decoration.HasChange ? AppColors.Status(decoration.Kind!.Value) : AppColors.Text),
            FontStyle = decoration.Actor == Actor.External ? FontStyle.Italic : FontStyle.Normal,
            TextDecorations = decoration.Strike ? TextDecorations.Strikethrough : null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(name);
        if (decoration.HasChange) row.Children.Add(Badge(decoration));
        if (decoration.Read)
        {
            row.Children.Add(new TextBlock
            {
                Text = Codicons.Glyph(Codicon.Eye),
                FontFamily = Codicons.Font,
                FontSize = 13,
                Foreground = AppColors.Brush(AppColors.Read),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        ToolTip.SetTip(row, decoration.Tooltip);
        Content = row;
    }

    /// <summary>
    /// Symbol (+ ~ −) with the color of the status. Filled = done by the agent; outline = external; dashed outline =
    /// unknown author.
    /// </summary>
    private static Control Badge(NodeDecoration decoration)
    {
        Color color = AppColors.Status(decoration.Kind!.Value);
        bool filled = decoration.Actor == Actor.Agent;
        var text = new TextBlock
        {
            Text = decoration.Badge,
            FontFamily = Mono,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = AppColors.Brush(filled ? AppColors.BadgeText : color),
            Margin = new Thickness(5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (decoration.Actor == Actor.Unknown)
        {
            var dashed = new Rectangle
            {
                Stroke = AppColors.Brush(color),
                StrokeThickness = 1,
                StrokeDashArray = [3, 2],
                RadiusX = 3,
                RadiusY = 3,
            };
            var panel = new Panel { VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(dashed);
            panel.Children.Add(text);
            return panel;
        }
        return new Border
        {
            Background = filled ? AppColors.Brush(color) : Brushes.Transparent,
            BorderBrush = AppColors.Brush(color),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = text,
        };
    }
}
