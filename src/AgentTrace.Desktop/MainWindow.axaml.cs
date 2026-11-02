using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AgentTrace.Core;
using AgentTrace.Desktop.Composition;
using AgentTrace.Desktop.Controls;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Graph;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Presentation.Search;
using AgentTrace.Core.Search;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop;

/// <summary>
/// The main window: top bar, project tree, six tabs and status bar. It only presents: what is
/// observed (traces, git, correlation) is done by <see cref="ProjectObserver"/>; the orchestration is in <c>MainWindow.Project.cs</c>.
/// </summary>
public sealed partial class MainWindow : Window
{
    public const string AppTitle = "AgentTrace Studio — AI Agent Observability";
    public const int CaptionRgb = 0x1C1C1E;
    public const int TextRgb = 0xF2F2F7;

    public const int GraphTab = 0;
    public const int DiffTab = 1;
    public const int TelemetryTab = 2;
    public const int FileTab = 3;
    public const int KnowledgeTab = 4;
    public const int HeatmapTab = 5;

    private readonly IReadOnlyList<IAgentAdapter> _adapters;
    private readonly RecentProjects _recent;
    private readonly LanguagePreference _language;
    private readonly MenuFlyout _languageMenu = new();
    private readonly Func<FilePath?, string?> _relaunch;
    private readonly TabControl _tabs = new();
    private readonly StatusBar _statusBar = new();
    private readonly TextBlock _projectPath = new() { Text = ShellStrings.NoProjectOpen, Foreground = AppColors.Brush(AppColors.Muted) };
    private readonly ProjectTreeView _tree;
    private readonly TelemetryPane _telemetry;
    private readonly DiffPane _diff = new();
    private readonly FileViewPane _file;
    private readonly GraphPane _graph;
    private readonly KnowledgePane _knowledge;
    private readonly HeatmapPane _heatmap;
    private readonly Button _terminalButton = new()
    {
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = Codicons.Glyph(Codicon.Terminal), FontFamily = Codicons.Font, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = ShellStrings.TerminalButton, VerticalAlignment = VerticalAlignment.Center },
            },
        },
        IsEnabled = false,
    };

    public MainWindow() : this(AdapterCatalog.All(), RecentProjects.Standard())
    {
    }

    /// <summary>
    /// With the given adapters and history (tests use fake agents and a temporary history).
    /// <paramref name="language"/> and <paramref name="relaunch"/> (by default the standard file and <see cref="AppRelauncher"/>)
    /// let tests exercise the language change without touching the user's profile or starting processes.
    /// </summary>
    public MainWindow(IReadOnlyList<IAgentAdapter> adapters, RecentProjects recent, LanguagePreference? language = null,
        Func<FilePath?, string?>? relaunch = null)
    {
        _adapters = adapters;
        _recent = recent;
        _language = language ?? LanguagePreference.Standard();
        _relaunch = relaunch ?? AppRelauncher.Relaunch;
        InitializeComponent();
        Icon = LoadIcon();
        _tree = new ProjectTreeView(path => OpenFile(path), ShowInInspector);
        _telemetry = new TelemetryPane(path => OpenFile(path));
        _file = new FileViewPane(ShowInInspector, path => _tree.Reveal(path));
        _graph = new GraphPane(path => OpenFile(path), ShowInInspector);
        _knowledge = new KnowledgePane(path => OpenFile(path), ShowInInspector);
        _heatmap = new HeatmapPane(path => OpenFile(path), ShowInInspector, path => _tree.Reveal(path), ShowInKnowledge);
        WireDiffPane();
        Content = BuildUi();
        _statusBar.Message = ShellStrings.StatusReady;
        _statusBar.SetStats(StatusSegments.Of(_stats));
        KeyDown += OnShortcut;
        Closing += (_, _) => Shutdown();
        // The knowledge graph and the heatmap are only recomputed with their tab in view: coming back to it brings them up to date
        _tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _tabs)) return;
            if (_tabs.SelectedIndex == KnowledgeTab) RefreshKnowledge();
            else if (_tabs.SelectedIndex == HeatmapTab) RefreshHeatmap();
        };

        // Avalonia may overwrite the DWM attributes when creating the window: they are applied after Opened.
        Opened += (_, _) =>
        {
            TitleBarDark = NativeTitleBar.ApplyDark(TryGetPlatformHandle()?.Handle ?? 0, CaptionRgb, TextRgb);
            ApplyStartupOptions();
            ScheduleCapture();
        };
    }

    public bool TitleBarDark { get; private set; }

    public TabControl Tabs => _tabs;

    public StatusBar StatusBar => _statusBar;

    public ProjectTreeView Tree => _tree;

    public TelemetryPane Telemetry => _telemetry;

    public DiffPane Diff => _diff;

    public FileViewPane FileView => _file;

    public GraphPane Graph => _graph;

    public KnowledgePane Knowledge => _knowledge;

    public HeatmapPane Heatmap => _heatmap;

    public FilePath? CurrentProject { get; private set; }

    /// <summary>The top bar menu that lists the supported languages (a check marks the current one).</summary>
    public MenuFlyout LanguageMenu => _languageMenu;

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://AgentTrace.Desktop/Assets/icons/app-256.png")));
        }
        catch (Exception e) when (e is IOException or FileNotFoundException)
        {
            return null;
        }
    }

    // ---- construction ----

    private Control BuildUi()
    {
        var root = new DockPanel { Background = AppColors.Brush(AppColors.Background) };

        Control toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        root.Children.Add(_statusBar);

        var header = new TextBlock { Text = ShellStrings.ProjectFilesHeader, FontWeight = FontWeight.Bold, Foreground = AppColors.Brush(AppColors.Text) };
        var left = new DockPanel { Margin = new Thickness(8) };
        var leftTop = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6), Children = { header, _projectPath } };
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        left.Children.Add(_tree);

        AddTab(ShellStrings.TabExecutionGraph, "Ctrl+1", _graph);
        AddTab(ShellStrings.TabDiffInspector, "Ctrl+2", _diff);
        AddTab(ShellStrings.TabProcessTelemetry, "Ctrl+3", _telemetry);
        AddTab(ShellStrings.TabFile, "Ctrl+4", _file);
        AddTab(ShellStrings.TabKnowledge, "Ctrl+5", _knowledge);
        AddTab(ShellStrings.TabHeatmap, "Ctrl+6", _heatmap);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("300,Auto,*") };
        split.Children.Add(left);
        var splitter = new GridSplitter
        {
            Width = 4,
            Background = AppColors.Brush(AppColors.BorderSoft),
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(splitter, 1);
        split.Children.Add(splitter);
        Grid.SetColumn(_tabs, 2);
        split.Children.Add(_tabs);
        root.Children.Add(split);
        return root;
    }

    private Control BuildToolbar()
    {
        var flyout = new MenuFlyout();
        ToolTip.SetTip(_terminalButton, ShellStrings.TerminalTooltip);
        _terminalButton.Click += (_, _) => OpenTerminal();
        var open = new SplitButton { Content = ShellStrings.OpenProjectButton, Flyout = flyout };
        open.Click += async (_, _) => await ShowOpenProjectDialogAsync();
        flyout.Opening += (_, _) => FillRecentMenu(flyout);
        return new Border
        {
            Background = AppColors.Brush(AppColors.Panel),
            BorderBrush = AppColors.Brush(AppColors.BorderSoft),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, _terminalButton, BuildLanguageButton() } },
        };
    }

    private Button BuildLanguageButton()
    {
        _languageMenu.Opening += (_, _) => FillLanguageMenu(_languageMenu);
        FillLanguageMenu(_languageMenu);
        return new Button { Content = ShellStrings.MenuLanguage, Flyout = _languageMenu };
    }

    private void FillLanguageMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        foreach (Language language in SupportedLanguages.All)
        {
            var item = new MenuItem
            {
                Header = language.NativeName,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "language",
                IsChecked = language.Code == Program.Language.Code,
            };
            item.Click += async (_, _) => await ChooseLanguageAsync(language);
            menu.Items.Add(item);
        }
    }

    /// <summary>Saves the chosen language and offers to restart to apply it (live switching is not supported). Does nothing if it is already the current one.</summary>
    public async Task ChooseLanguageAsync(Language language)
    {
        if (language.Code == Program.Language.Code) return;
        _language.Save(language);
        if (!await Dialogs.AskRestartAsync(this, language.NativeName)) return;
        if (_relaunch(CurrentProject) is { } reason)
        {
            _statusBar.Message = string.Format(CultureInfo.CurrentCulture, ShellStrings.RestartFailedFormat, reason);
            return;
        }
        Close();
    }

    private void AddTab(string title, string shortcut, Control content)
    {
        var tab = new TabItem { Header = title, Content = content };
        ToolTip.SetTip(tab, shortcut);
        _tabs.Items.Add(tab);
    }

    private static Control Placeholder(string text) => new TextBlock
    {
        Text = text,
        Foreground = AppColors.Brush(AppColors.Muted),
        Margin = new Thickness(16),
    };

    // ---- recent projects and dialog ----

    private void FillRecentMenu(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        IReadOnlyList<FilePath> recents = _recent.List();
        if (recents.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = ShellStrings.NoRecentProjects, IsEnabled = false });
            return;
        }
        foreach (FilePath path in recents)
        {
            var item = new MenuItem { Header = path.Value };
            item.Click += (_, _) => OpenProject(path);
            flyout.Items.Add(item);
        }
    }

    /// <summary>Opens the last project that was used, if there is one (when starting without specifying one).</summary>
    public bool OpenLastProject()
    {
        FilePath? last = _recent.Last();
        if (last is { } project) OpenProject(project);
        return last is not null;
    }

    public async Task ShowOpenProjectDialogAsync()
    {
        var options = new FolderPickerOpenOptions { Title = ShellStrings.OpenProjectDialogTitle, AllowMultiple = false };
        if (_recent.Last() is { } last) options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(last.Value);
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(options);
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path && Directory.Exists(path)) OpenProject(new FilePath(path));
    }

    public void SwitchTab(int index) => _tabs.SelectedIndex = Math.Clamp(index, 0, _tabs.ItemCount - 1);

    // ---- shortcuts and arguments ----

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        int? tab = e.Key switch
        {
            Key.D1 => GraphTab,
            Key.D2 => DiffTab,
            Key.D3 => TelemetryTab,
            Key.D4 => FileTab,
            Key.D5 => KnowledgeTab,
            Key.D6 => HeatmapTab,
            _ => null,
        };
        if (tab is { } index)
        {
            SwitchTab(index);
            e.Handled = true;
        }
        else if (e.Key == Key.T)
        {
            OpenTerminal();
            e.Handled = true;
        }
        else if (e.Key == Key.K)
        {
            ShowSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.O)
        {
            _ = ShowOpenProjectDialogAsync();
            e.Handled = true;
        }
    }

    private void ApplyStartupOptions()
    {
        StartupOptions options = Program.Options;
        if (options.Project is { } project) OpenProject(project);
        else OpenLastProject();
        // --open=<path>: opens that file in the "File" tab (useful to check the viewer without using the mouse)
        if (options.Open is { } open) OpenFile(open);
        // --diff=<path>: selects that file's diff in the Inspector when it appears among the changes
        if (options.Diff is { } diff) ShowDiffOf(diff);
        // To check the graph without using the mouse: --graph-select, --graph-collapse, --graph-orientation, --graph-filter, --graph-zoom
        if (options.KnowledgeSelect is { } kgSelect) SelectInKnowledge(kgSelect);
        if (options.Search is { } search) ShowSearch(search);
        if (options.KnowledgeView is { } kgView) _knowledge.ShowView(kgView);
        if (options.KnowledgeYaw is not null || options.KnowledgePitch is not null)
        {
            Camera3D cam = _knowledge.Canvas.Camera;
            cam.SetView((options.KnowledgeYaw is { } y ? y : cam.Yaw * 180 / Math.PI) * Math.PI / 180,
                (options.KnowledgePitch is { } p ? p : cam.Pitch * 180 / Math.PI) * Math.PI / 180);
        }
        if (options.GraphSelect is { } graphSelect) SelectInGraph(graphSelect);
        if (options.GraphCollapse is { } collapse) _graph.Canvas.SetAutoFold(!"none".Equals(collapse, StringComparison.OrdinalIgnoreCase));
        if (options.GraphOrientation is { } orientation)
        {
            _graph.ShowOrientation("horizontal".Equals(orientation, StringComparison.OrdinalIgnoreCase) ? GraphOrientation.Horizontal : GraphOrientation.Vertical);
        }
        if (options.GraphFilter is { } filter)
        {
            string f = filter.ToLowerInvariant();
            _graph.ShowFilter(new GraphFilter(f.Contains("errors", StringComparison.Ordinal), f.Contains("reads", StringComparison.Ordinal)));
        }
        if (options.GraphZoom is { } zoom) _graph.Canvas.SetZoom(zoom);
        // To check the heatmap without using the mouse: --heatmap-filter, --heatmap-area, --heatmap-zoom, --heatmap-select
        if (options.HeatmapFilter is { } heatFilter) _heatmap.SetFilter(ActivityFilter.Parse(heatFilter));
        if (options.HeatmapArea is { } heatArea) _heatmap.SetArea("activity".Equals(heatArea, StringComparison.OrdinalIgnoreCase) ? HeatArea.Activity : HeatArea.Size);
        if (options.HeatmapZoom is not null || options.HeatmapSelect is not null) SetHeatmapPending(options.HeatmapZoom, options.HeatmapSelect);
        if (options.Tab is { } tab)
        {
            SwitchTab(tab switch { "graph" => GraphTab, "diff" => DiffTab, "telemetry" => TelemetryTab, "file" => FileTab, "knowledge" => KnowledgeTab, "heatmap" => HeatmapTab, _ => GraphTab });
        }
    }

    private void ScheduleCapture()
    {
        StartupOptions options = Program.Options;
        if (options.Capture is not { } path) return;
        DispatcherTimer.RunOnce(() =>
        {
            if (options.PrintWindowCapture)
            {
                bool ok = WindowCapture.SavePrintWindow(this, path);
                Console.WriteLine($"capture printwindow ok={ok} titlebarDark={TitleBarDark}");
            }
            else
            {
                WindowCapture.SaveRenderTarget(this, path);
                Console.WriteLine("capture rendertarget ok");
            }
            Close();
        }, TimeSpan.FromMilliseconds(options.CaptureDelayMillis));
    }
}
