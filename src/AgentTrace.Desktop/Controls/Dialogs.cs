using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Desktop.Controls;

/// <summary>The application's modal dialogs (Avalonia has no <c>Alert</c>).</summary>
public static class Dialogs
{
    /// <summary>
    /// Offers to create the git repository. It modifies the project folder, so it is only done if the user confirms.
    /// Returns null if the user chooses to continue without git; otherwise, whether they also want a first commit.
    /// </summary>
    public static async Task<bool?> AskGitInitAsync(Window owner, string projectName)
    {
        var commit = new CheckBox
        {
            Content = new TextBlock { Text = ShellStrings.GitInitCommitCheckbox, TextWrapping = TextWrapping.Wrap },
        };
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = ShellStrings.GitInitDialogMessage,
        };
        var dialog = Dialog(ShellStrings.GitInitDialogTitle, string.Format(CultureInfo.CurrentCulture, ShellStrings.GitInitDialogHeaderFormat, projectName), [text, commit], 600);
        bool? result = null;
        bool created = false;
        AddButtons(dialog, (ShellStrings.GitInitCreate, () => { created = true; }), (ShellStrings.GitInitSkip, () => { }));
        await dialog.ShowDialog(owner);
        if (created) result = commit.IsChecked == true;
        return result;
    }

    /// <summary>Asks whether to restart now to apply the new language; true for "Restart now", false for "Later" (or closing the dialog).</summary>
    public static async Task<bool> AskRestartAsync(Window owner, string languageName)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = string.Format(CultureInfo.CurrentCulture, ShellStrings.LanguageRestartMessageFormat, languageName),
        };
        var dialog = Dialog(ShellStrings.LanguageRestartTitle, ShellStrings.LanguageRestartTitle, [text], 460);
        bool restart = false;
        AddButtons(dialog, (ShellStrings.RestartNow, () => { restart = true; }), (ShellStrings.RestartLater, () => { }));
        await dialog.ShowDialog(owner);
        return restart;
    }

    public static Task ShowErrorAsync(Window owner, string header, string message)
    {
        var dialog = Dialog(CommonStrings.Error, header, [new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }], 520);
        AddButtons(dialog, (CommonStrings.Ok, () => { }));
        return dialog.ShowDialog(owner);
    }

    private static Window Dialog(string title, string header, IEnumerable<Control> body, double width)
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = header, FontSize = 16, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        foreach (Control c in body) stack.Children.Add(c);
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = AppColors.Brush(AppColors.Card),
            Content = stack,
        };
    }

    private static void AddButtons(Window dialog, params (string Text, Action OnClick)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        foreach ((string text, Action onClick) in buttons)
        {
            var button = new Button { Content = text };
            button.Click += (_, _) =>
            {
                onClick();
                dialog.Close();
            };
            row.Children.Add(button);
        }
        ((StackPanel)dialog.Content!).Children.Add(row);
    }
}
