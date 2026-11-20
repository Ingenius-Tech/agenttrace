using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AgentTrace.Core;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Observation;
using AgentTrace.Testing;

namespace AgentTrace.Desktop.Tests;

/// <summary>The recent-projects drop-down next to the "Open project" button.</summary>
public sealed class RecentProjectsMenuTests : TempDirTestBase
{
    [Fact]
    public Task TheDropDownArrowOpensTheRecentProjectsAndChoosingOneSwitchesToIt() => HeadlessHost.Run(() =>
    {
        FilePath first = Dir.Combine("first");
        FilePath second = Dir.Combine("second");
        Directory.CreateDirectory(first.Value);
        Directory.CreateDirectory(second.Value);
        var recent = new RecentProjects(Dir.Combine("recent.txt"));
        recent.Add(first);
        recent.Add(second);
        var window = new MainWindow([], recent, new LanguagePreference(Dir.Combine("prefs", "language.txt")),
            _ => null, new PanoramaPreference(Dir.Combine("prefs", "panorama.txt")));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        SplitButton open = window.GetLogicalDescendants().OfType<SplitButton>().Single();
        Button arrow = open.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_SecondaryButton");
        arrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var flyout = Assert.IsType<MenuFlyout>(open.Flyout);
        Assert.True(flyout.IsOpen);
        MenuItem[] items = [.. flyout.Items.OfType<MenuItem>()];
        Assert.Equal(["second", "first"], items.Select(i => i.Header));

        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(first, window.CurrentProject);
        window.Close();
    });
}
