using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AgentTrace.Core;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Resources;
using AgentTrace.Testing;

namespace AgentTrace.Desktop.Tests;

/// <summary>The language selector of the top bar: menu, saved preference and the "Restart now / Later" dialog (no process is started).</summary>
public sealed class LanguageSelectorTests : TempDirTestBase
{
    private FilePath PreferenceFile => Dir.Combine("prefs", "language.txt");

    private MainWindow NewWindow(List<FilePath?> relaunches, string? relaunchFailure = null) =>
        new([], new RecentProjects(Dir.Combine("recent.txt")), new LanguagePreference(PreferenceFile),
            project =>
            {
                relaunches.Add(project);
                return relaunchFailure;
            });

    private static Window OpenDialog(MainWindow window) => Assert.Single(window.OwnedWindows);

    private static void Click(Window dialog, string text)
    {
        Button button = dialog.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task TheMenuListsTheLanguagesByNativeNameAndChecksTheCurrentOne() => HeadlessHost.Run(() =>
    {
        var window = NewWindow([]);
        window.Show();
        MenuItem[] items = [.. window.LanguageMenu.Items.OfType<MenuItem>()];
        Assert.Equal(["English", "Español"], items.Select(i => i.Header)); // lang-ok
        Assert.Equal([true, false], items.Select(i => i.IsChecked)); // the tests run in English
        window.Close();
    });

    [Fact]
    public Task TheTopBarHasAButtonWithTheLocalizedLabel() => HeadlessHost.Run(() =>
    {
        var window = NewWindow([]);
        window.Show();
        Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == ShellStrings.MenuLanguage);
        Assert.Equal("Language", ShellStrings.MenuLanguage);
        window.Close();
    });

    [Fact]
    public Task ChoosingTheCurrentLanguageDoesNothing() => HeadlessHost.Run(() =>
    {
        List<FilePath?> relaunches = [];
        var window = NewWindow(relaunches);
        window.Show();
        Task choose = window.ChooseLanguageAsync(SupportedLanguages.English);
        Dispatcher.UIThread.RunJobs();
        Assert.True(choose.IsCompletedSuccessfully);
        Assert.Empty(window.OwnedWindows);
        Assert.False(File.Exists(PreferenceFile.Value));
        Assert.Empty(relaunches);
        window.Close();
    });

    [Fact]
    public Task LaterSavesThePreferenceAndKeepsTheWindowOpen() => HeadlessHost.Run(() =>
    {
        List<FilePath?> relaunches = [];
        var window = NewWindow(relaunches);
        window.Show();
        Task choose = window.ChooseLanguageAsync(SupportedLanguages.Spanish);
        Dispatcher.UIThread.RunJobs();
        Assert.False(choose.IsCompleted); // the dialog is open
        Assert.Equal("es", new LanguagePreference(PreferenceFile).Load()); // saved before asking
        Window dialog = OpenDialog(window);
        Assert.Equal(ShellStrings.LanguageRestartTitle, dialog.Title);
        Click(dialog, ShellStrings.RestartLater);
        Assert.True(choose.IsCompletedSuccessfully);
        Assert.Empty(relaunches);
        Assert.True(window.IsVisible);
        window.Close();
    });

    [Fact]
    public Task RestartNowRelaunchesWithTheCurrentProjectAndClosesTheWindow() => HeadlessHost.Run(() =>
    {
        List<FilePath?> relaunches = [];
        var window = NewWindow(relaunches);
        window.Show();
        Task choose = window.ChooseLanguageAsync(SupportedLanguages.Spanish);
        Dispatcher.UIThread.RunJobs();
        Click(OpenDialog(window), ShellStrings.RestartNow);
        Assert.True(choose.IsCompletedSuccessfully);
        Assert.Equal([(FilePath?)null], relaunches); // no project was open
        Assert.False(window.IsVisible);
        Assert.Equal("es", new LanguagePreference(PreferenceFile).Load());
    });

    [Fact]
    public Task AFailedRelaunchKeepsTheWindowAndSaysWhy() => HeadlessHost.Run(() =>
    {
        var window = NewWindow([], relaunchFailure: "no permission");
        window.Show();
        Task choose = window.ChooseLanguageAsync(SupportedLanguages.Spanish);
        Dispatcher.UIThread.RunJobs();
        Click(OpenDialog(window), ShellStrings.RestartNow);
        Assert.True(choose.IsCompletedSuccessfully);
        Assert.True(window.IsVisible);
        Assert.Equal("Could not restart: no permission", window.StatusBar.Message);
        window.Close();
    });

    [Fact]
    public Task TheDialogIsShownInSpanishWhenTheUiThreadRunsInSpanish() => HeadlessHost.Run(() =>
    {
        using var _ = CultureScope.Spanish();
        var window = NewWindow([]);
        window.Show();
        Task choose = window.ChooseLanguageAsync(SupportedLanguages.Spanish);
        Dispatcher.UIThread.RunJobs();
        Window dialog = OpenDialog(window);
        Assert.Contains("Reiniciar ahora", dialog.GetLogicalDescendants().OfType<Button>().Select(b => b.Content as string)); // lang-ok
        Click(dialog, "Más tarde"); // lang-ok
        Assert.True(choose.IsCompletedSuccessfully);
        window.Close();
    });
}
