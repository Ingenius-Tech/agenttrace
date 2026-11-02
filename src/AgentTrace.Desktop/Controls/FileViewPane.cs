using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>"File" tab: header with the data of the file and the read-only viewer. Presentation only.</summary>
public sealed class FileViewPane : UserControl
{
    private readonly TextBlock _title = new() { Text = TextStrings.FileNoneOpen, FontWeight = FontWeight.Bold, Foreground = AppColors.Brush(AppColors.Text) };
    private readonly TextBlock _status = new() { FontStyle = FontStyle.Italic, Foreground = AppColors.Brush(AppColors.Muted), IsVisible = false };
    private readonly TextBlock _details = new() { Foreground = AppColors.Brush(AppColors.Muted) };
    private readonly StackPanel _warnings = new() { Spacing = 2 };
    private readonly TextBlock _placeholder = new() { Foreground = AppColors.Brush(AppColors.Muted), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly CodeViewer _viewer = new();
    private readonly Button _changesButton = new() { Content = TextStrings.ActionShowChanges, IsEnabled = false };
    private readonly Button _revealButton = new() { Content = TextStrings.RevealInTree, IsEnabled = false };

    public FileViewPane(Action<FilePath> onShowChanges, Action<FilePath> onReveal)
    {
        _changesButton.Click += (_, _) =>
        {
            if (Current is { } file) onShowChanges(file);
        };
        _revealButton.Click += (_, _) =>
        {
            if (Current is { } file) onReveal(file);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _changesButton, _revealButton } };
        var top = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _title, _status }, VerticalAlignment = VerticalAlignment.Center });

        var content = new Grid();
        content.Children.Add(_viewer);
        content.Children.Add(_placeholder);

        var header = new StackPanel { Spacing = 6, Children = { top, _details, _warnings } };
        var root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(content);
        Content = root;
    }

    /// <summary>The open file, or null.</summary>
    public FilePath? Current { get; private set; }

    public CodeViewer Viewer => _viewer;

    /// <summary>
    /// Shows the content of a file. <paramref name="statusText"/> is the status in the project ("Modified · by the agent"), or null;
    /// <paramref name="hasChanges"/> enables "View changes". With <paramref name="keepPosition"/> the cursor is kept.
    /// </summary>
    public void Show(FilePath projectDir, FileContent content, string? statusText, bool hasChanges, bool keepPosition)
    {
        bool sameFile = content.Path == Current;
        Current = content.Path;
        _title.Text = ChangeFormatter.Relative(projectDir, content.Path);
        SetStatus(statusText);
        _details.Text = FileHeader.Details(content);
        _warnings.Children.Clear();
        foreach (string warning in FileHeader.Warnings(content))
        {
            _warnings.Children.Add(new TextBlock { Text = "⚠ " + warning, Foreground = AppColors.Brush(AppColors.Modified) });
        }
        string? message = FileHeader.Placeholder(content);
        _placeholder.Text = message ?? "";
        _placeholder.IsVisible = message is not null;
        _viewer.IsVisible = message is null;
        if (message is null) _viewer.ShowText(content.Lines, content.Path.FileName, keepPosition && sameFile);
        else _viewer.ShowText([], "");
        _changesButton.IsEnabled = hasChanges;
        _revealButton.IsEnabled = true;
    }

    /// <summary>Updates only the status of the open file (the content has not changed, but what is known about it has).</summary>
    public void UpdateStatus(string? statusText, bool hasChanges)
    {
        if (Current is null) return;
        SetStatus(statusText);
        _changesButton.IsEnabled = hasChanges;
    }

    public void Clear()
    {
        Current = null;
        _title.Text = TextStrings.FileNoneOpen;
        SetStatus(null);
        _details.Text = "";
        _warnings.Children.Clear();
        _placeholder.IsVisible = false;
        _viewer.IsVisible = true;
        _viewer.ShowText([], "");
        _changesButton.IsEnabled = false;
        _revealButton.IsEnabled = false;
    }

    private void SetStatus(string? text)
    {
        _status.Text = text ?? "";
        _status.IsVisible = text is not null;
    }
}
