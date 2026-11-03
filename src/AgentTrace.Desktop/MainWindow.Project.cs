using System.Globalization;
using Avalonia.Threading;
using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Core.Knowledge;
using AgentTrace.Core.Search;
using AgentTrace.Desktop.Composition;
using AgentTrace.Desktop.Controls;
using AgentTrace.Git;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Graph;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Search;
using AgentTrace.Presentation.Text;
using AgentTrace.Presentation.Tree;

namespace AgentTrace.Desktop;

/// <summary>
/// The window's orchestration: opens a project, receives what <see cref="ProjectObserver"/> observes from background threads
/// and passes it to the UI thread. All the domain logic is in Presentation and Core; here it is only wired together.
/// </summary>
public sealed partial class MainWindow
{
    private ProjectObserver? _observer;
    private SessionStats _stats = new();

    /// <summary>Incremented when a project is opened to discard late notifications from the previous observer.</summary>
    private long _generation;

    private IReadOnlyList<ChangeEntry> _lastChanges = [];
    private FileStatusIndex _lastIndex = FileStatusIndex.Empty(new FilePath("."));
    private bool _treeRefreshPending;

    /// <summary>The ignore rules of the last tree pass: the heatmap reuses them without calling git again.</summary>
    private IgnoreRules? _lastRules;

    /// <summary>File whose diff was requested by argument: it is shown as soon as it appears in the change list.</summary>
    private FilePath? _pendingDiff;

    /// <summary>Signature (size and date) of the open file when it was read, so it is not re-read if it has not changed.</summary>
    private string _openFileSignature = "";

    public ProjectObserver? Observer => _observer;

    public SessionStats Stats => _stats;

    public FileStatusIndex LastIndex => _lastIndex;

    // ---- opening a project ----

    public void OpenProject(FilePath project)
    {
        if (_observer is { } old) Task.Run(old.Dispose);
        long mine = ++_generation;
        CurrentProject = project;
        _terminalButton.IsEnabled = true;
        _recent.Add(project);
        _projectPath.Text = project.FileName.Length == 0 ? project.Value : project.FileName;
        _stats = new SessionStats();
        _statusBar.Message = string.Format(CultureInfo.CurrentCulture, ShellStrings.StatusMonitoringFormat, project);
        _statusBar.SetStats(StatusSegments.Of(_stats));
        ResetAnalysis();
        _telemetry.Clear();
        _diff.Reset(project);
        _diff.SetGitStatus(ShellStrings.DiffGitSearching);
        _lastChanges = [];
        _lastIndex = FileStatusIndex.Empty(project);
        _treeRefreshPending = false;
        _lastRules = null;
        _openFileSignature = "";
        _file.Clear();
        _tree.SetProject(project);

        _observer = new ProjectObserver(project, _adapters, new UiListener(this, mine));
        _statusBar.Message = string.Format(CultureInfo.CurrentCulture, ShellStrings.StatusWaitingFormat, project.FileName.Length == 0 ? project.Value : project.FileName);
        // Locating the repository and taking the baseline runs git: off the UI thread
        ProjectObserver started = _observer;
        Task.Run(started.Start);
        SwitchTab(GraphTab);
    }

    /// <summary>Passes an observer notification to the UI thread, if the project is still the same.</summary>
    private void OnUi(long mine, Action action) => Dispatcher.UIThread.Post(() =>
    {
        if (mine == _generation) action();
    });

    private sealed class UiListener(MainWindow window, long mine) : IProjectObserverListener
    {
        public void SessionChanged(string agent, FilePath sessionTrace) => window.OnUi(mine, () =>
        {
            window._telemetry.Clear();
            window._stats = new SessionStats();
            window._statusBar.SetStats(StatusSegments.Of(window._stats));
            window.ResetAnalysis();
            window._statusBar.Message = string.Format(CultureInfo.CurrentCulture, ShellStrings.StatusSessionFormat, agent, sessionTrace.FileName);
        });

        public void Events(IReadOnlyList<AgentEvent> events) => window.OnUi(mine, () => window.ShowEvents(events));

        public void Changes(IReadOnlyList<ChangeEntry> changes) => window.OnUi(mine, () =>
        {
            window._lastChanges = changes;
            window.OnChangesForAnalysis(changes);
            window.ShowChanges(changes);
            window.ScheduleTreeRefresh();
        });

        public void GitStateChanged(GitState state, string detail) => window.OnUi(mine, () =>
        {
            window.ShowGitState(state, detail);
            window.ScheduleTreeRefresh();
        });

        public void FilesChanged() => window.OnUi(mine, window.ScheduleTreeRefresh);

        public void Error(string message) => window.OnUi(mine, () => window._statusBar.Message = message);
    }

    // ---- telemetry ----

    private void ShowEvents(IReadOnlyList<AgentEvent> events)
    {
        List<TelemetryRow> lines = [];
        foreach (AgentEvent evt in events)
        {
            _stats.Accept(evt);
            if (EventFormatter.Line(evt) is { } line) lines.Add(new TelemetryRow(line, EventFormatter.PathOf(evt)));
        }
        _telemetry.AddAtTop(lines);
        _statusBar.SetStats(StatusSegments.Of(_stats));
        OnEventsForAnalysis(events);
    }

    // ---- the tree ----

    /// <summary>
    /// Requests a repaint of the tree. Bursts of notifications are grouped (300 ms) and the work that queries git (what is ignored)
    /// is done off the UI thread.
    /// </summary>
    private void ScheduleTreeRefresh()
    {
        if (_treeRefreshPending || _observer is null) return;
        _treeRefreshPending = true;
        long mine = _generation;
        ProjectObserver current = _observer;
        FilePath project = CurrentProject!.Value;
        DispatcherTimer.RunOnce(() =>
        {
            _treeRefreshPending = false;
            IReadOnlyList<ChangeEntry> changes = _lastChanges;
            Task.Run(() =>
            {
                IgnoreRules rules = current.IgnoreRules();
                var index = FileStatusIndex.Of(project, changes, current.Reads());
                OnUi(mine, () =>
                {
                    _lastIndex = index;
                    _lastRules = rules;
                    _heatmapDiskDirty = true;
                    _tree.Update(rules, index);
                    ScheduleHeatmapRefresh();
                    RefreshOpenFileStatus();
                    ReloadOpenFile(false);
                });
            });
        }, TimeSpan.FromMilliseconds(300));
    }

    // ---- the open file ----

    private FilePath ResolveInProject(string path) =>
        Path.IsPathRooted(path) || CurrentProject is null ? new FilePath(path) : CurrentProject.Value.Combine(path);

    /// <summary>Opens the system terminal in the project folder; if it cannot, it says so in the status bar.</summary>
    public void OpenTerminal()
    {
        if (CurrentProject is not { } project) return;
        if (TerminalLauncher.Open(project) is { } problem) _statusBar.Message = problem;
    }

    public void OpenFile(string path) => OpenFile(ResolveInProject(path));

    public void OpenFile(FilePath file)
    {
        _openFileSignature = "";
        LoadFile(file, false);
        SwitchTab(FileTab);
    }

    /// <summary>The status of the open file (modified, read...) can change without its content changing.</summary>
    private void RefreshOpenFileStatus()
    {
        if (_file.Current is not { } path) return;
        NodeDecoration decoration = NodeDecoration.Of(new FileNode(path, false, false, false), _lastIndex);
        _file.UpdateStatus(decoration.Tooltip, decoration.HasChange);
    }

    /// <summary>Re-reads the open file if it changed on disk (and only then).</summary>
    private void ReloadOpenFile(bool force)
    {
        if (_file.Current is not { } path) return;
        if (!force && Signature(path) == _openFileSignature) return;
        LoadFile(path, true);
    }

    private void LoadFile(FilePath path, bool keepPosition)
    {
        long mine = _generation;
        FilePath? project = CurrentProject;
        Task.Run(() =>
        {
            string signature = Signature(path);
            FileContent content = FileContentReader.Read(path);
            OnUi(mine, () =>
            {
                _openFileSignature = signature;
                NodeDecoration decoration = NodeDecoration.Of(new FileNode(path, false, false, false), _lastIndex);
                _file.Show(project ?? path.Parent ?? path, content, decoration.Tooltip, decoration.HasChange, keepPosition);
            });
        });
    }

    private static string Signature(FilePath path)
    {
        try
        {
            var info = new FileInfo(path.Value);
            return info.Length + ":" + new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        }
        catch (IOException)
        {
            return "missing";
        }
    }

    // ---- changes and diffs ----

    private void WireDiffPane()
    {
        _diff.SelectionChanged += ShowDiff;
        _diff.OpenRequested += path => OpenFile(path);
        _diff.RevealRequested += path => _tree.Reveal(path);
        _diff.BaselineChanged += mode =>
        {
            if (_observer is { } observer) Task.Run(() => observer.SetMode(mode));
        };
        _diff.ResetRequested += () =>
        {
            if (_observer is { } observer) Task.Run(observer.ResetBaseline);
        };
    }

    /// <summary>Requests that this file's diff (path relative to the project, or absolute) be shown as soon as it has changes.</summary>
    public void ShowDiffOf(string file)
    {
        _pendingDiff = ResolveInProject(file);
        SwitchTab(DiffTab);
    }

    private void ShowChanges(IReadOnlyList<ChangeEntry> changes)
    {
        FilePath? preferred = _pendingDiff;
        _diff.SetChanges(changes, preferred);
        if (preferred is { } wanted && changes.Any(c => c.Change.Path == wanted)) _pendingDiff = null;
    }

    private void ShowDiff(ChangeEntry? entry)
    {
        if (entry is null || _observer is null || CurrentProject is null)
        {
            _diff.ShowMessage(ShellStrings.DiffSelectFile);
            return;
        }
        ProjectObserver current = _observer;
        long mine = _generation;
        FilePath path = entry.Change.Path;
        FilePath project = CurrentProject.Value;
        Task.Run(() =>
        {
            IReadOnlyList<DiffRow> rows;
            try
            {
                rows = DiffFormatter.Rows(current.Versions(path), ChangeFormatter.Relative(project, path));
            }
            catch (IOException e)
            {
                rows = [DiffRow.Message(string.Format(CultureInfo.CurrentCulture, ShellStrings.DiffReadFailedFormat, e.Message))];
            }
            OnUi(mine, () =>
            {
                if (_diff.Selected?.Change.Path == path) _diff.ShowDiff(rows, path.FileName);
            });
        });
    }

    /// <summary>Selects the file in the Inspector's change list and shows that tab.</summary>
    private void ShowInInspector(FilePath path)
    {
        if (_diff.Select(path))
        {
            SwitchTab(DiffTab);
            return;
        }
        _statusBar.Message = string.Format(CultureInfo.CurrentCulture, ShellStrings.StatusNoChangesInInspectorFormat, path.FileName);
    }

    // ---- git ----

    private void ShowGitState(GitState state, string detail)
    {
        switch (state)
        {
            case GitState.Ready:
                _diff.SetGitStatus(string.Format(CultureInfo.CurrentCulture, ShellStrings.GitStatusRepositoryFormat, detail));
                break;
            case GitState.MultipleRepositories:
                _diff.SetGitStatus(string.Format(CultureInfo.CurrentCulture, ShellStrings.GitStatusMultipleFormat, detail));
                break;
            case GitState.NoGitProgram or GitState.Error:
                _diff.SetGitStatus(detail);
                break;
            case GitState.NoRepository:
                _diff.SetGitStatus(detail);
                _ = OfferGitInitAsync();
                break;
        }
    }

    /// <summary>Offers to create the repository. It modifies the project folder, so it is only done if the user confirms.</summary>
    private async Task OfferGitInitAsync()
    {
        if (_observer is not { } current || CurrentProject is not { } project) return;
        long mine = _generation;
        bool? withCommit = await Dialogs.AskGitInitAsync(this, project.FileName.Length == 0 ? project.Value : project.FileName);
        if (withCommit is not { } commit) return;
        _diff.SetGitStatus(ShellStrings.GitStatusCreating);
        try
        {
            await Task.Run(() => current.InitRepository(commit));
        }
        catch (IOException e)
        {
            if (mine != _generation) return;
            _diff.SetGitStatus(string.Format(CultureInfo.CurrentCulture, ShellStrings.GitInitFailedFormat, e.Message));
            await Dialogs.ShowErrorAsync(this, ShellStrings.GitInitFailedTitle, e.Message);
        }
    }

    // ---- analysis (execution graph and knowledge graph) ----

    /// <summary>The session history for the graph; only touched from the UI thread.</summary>
    private FlowBuilder _flow = new();

    private bool _graphRefreshPending;

    /// <summary>Selection requested by argument: applied as soon as it exists in the graph.</summary>
    private string? _pendingGraphSelect;

    /// <summary>What the project files have touched, for the knowledge graph.</summary>
    private KnowledgeBuilder _knowledgeBuilder = new();

    private bool _knowledgeRefreshPending;
    private bool _knowledgeDirty;
    private string? _pendingKnowledgeSelect;

    /// <summary>What happened in the session, for the search window (Ctrl+K).</summary>
    private SearchIndex _searchIndex = new();

    private SearchWindow? _searchWindow;
    private bool _searchRefreshPending;

    private void ResetAnalysis()
    {
        _searchIndex = new SearchIndex();
        _searchWindow?.Refresh();
        _flow = new FlowBuilder();
        _knowledgeBuilder = new KnowledgeBuilder(CurrentProject);
        _knowledgeBuilder.SetChanges(_lastChanges);
        _knowledge.SetProjectRoot(CurrentProject);
        _knowledge.SetGraph(_knowledgeBuilder.Snapshot());
        _graph.SetGraph(_flow.Snapshot());
        _heatmap.SetProjectRoot(CurrentProject);
        _heatmap.Clear();
        _heatmapBusy = false;
        _heatmapDirty = true;
        _heatmapDiskDirty = true;
        _heatmapListing = null;
        _pendingHeatmapZoom = null;
        _pendingHeatmapSelect = null;
    }

    private void OnEventsForAnalysis(IReadOnlyList<AgentEvent> events)
    {
        foreach (AgentEvent evt in events)
        {
            _flow.Accept(evt);
            _knowledgeBuilder.Accept(evt);
            _searchIndex.Accept(evt);
        }
        ScheduleSearchRefresh();
        ScheduleGraphRefresh();
        ScheduleKnowledgeRefresh();
        ScheduleHeatmapRefresh();
    }

    /// <summary>With the search window open, a new result appears without repeating the search (at most every 500 ms).</summary>
    private void ScheduleSearchRefresh()
    {
        if (_searchWindow is null || _searchRefreshPending) return;
        _searchRefreshPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _searchRefreshPending = false;
            _searchWindow?.Refresh();
        }, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Opens (or brings to the front) the session search window; with text, types it and searches (<c>--search</c>).</summary>
    public SearchWindow ShowSearch(string? text = null)
    {
        if (_searchWindow is null)
        {
            var window = new SearchWindow((query, limit) => _searchIndex.Search(query, limit), JumpTo);
            window.Closed += (_, _) => _searchWindow = null;
            _searchWindow = window;
            window.Show(this);
        }
        else _searchWindow.Activate();
        if (text is not null) _searchWindow.QueryText = text;
        _searchWindow.Refresh();
        return _searchWindow;
    }

    public SearchWindow? SearchWindow => _searchWindow;

    /// <summary>Does what Enter would do on the first result (tests do not have the keyboard inside the list).</summary>
    public void JumpToFirstSearchResult()
    {
        if (_searchWindow is { Hits: [var first, ..] }) JumpTo(first);
    }

    /// <summary>Goes to a search result: its step or turn in the execution graph, or the file in the viewer.</summary>
    private void JumpTo(SearchHit hit)
    {
        switch (JumpTarget.Of(hit.Entry))
        {
            case JumpTarget.File file:
                OpenFile(file.Path);
                break;
            case JumpTarget.Step step:
                SwitchTab(GraphTab);
                SelectInGraph(step.CallId);
                break;
            case JumpTarget.Turn turn:
                SwitchTab(GraphTab);
                SelectInGraph("turn:" + turn.Index);
                break;
            default:
                throw new System.Diagnostics.UnreachableException();
        }
        Activate();
    }

    /// <summary>Repaints the knowledge graph at most once a second, and only if its tab is in view.</summary>
    private void ScheduleKnowledgeRefresh()
    {
        _knowledgeDirty = true;
        if (_knowledgeRefreshPending || _tabs.SelectedIndex != KnowledgeTab) return;
        _knowledgeRefreshPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _knowledgeRefreshPending = false;
            RefreshKnowledge();
        }, TimeSpan.FromMilliseconds(1000));
    }

    private void RefreshKnowledge()
    {
        if (!_knowledgeDirty) return;
        _knowledgeDirty = false;
        _knowledge.SetGraph(_knowledgeBuilder.Snapshot());
        ApplyPendingKnowledgeSelect();
    }

    // ---- heatmap ----

    private bool _heatmapRefreshPending;
    private bool _heatmapBusy;

    /// <summary>There is new data since the last paint.</summary>
    private bool _heatmapDirty = true;

    /// <summary>The disk must be listed again (the observer reported changes); otherwise the last listing is reused.</summary>
    private bool _heatmapDiskDirty = true;

    private ProjectListing? _heatmapListing;
    private string? _pendingHeatmapZoom;
    private string? _pendingHeatmapSelect;

    /// <summary>Repaints the heatmap at most once a second, and only if its tab is in view.</summary>
    private void ScheduleHeatmapRefresh()
    {
        _heatmapDirty = true;
        if (_heatmapRefreshPending || _tabs.SelectedIndex != HeatmapTab) return;
        _heatmapRefreshPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _heatmapRefreshPending = false;
            RefreshHeatmap();
        }, TimeSpan.FromMilliseconds(1000));
    }

    /// <summary>
    /// Rebuilds the map's tree: the disk listing and the construction run on a separate thread (with the tree's ignore
    /// rules); the graph is taken here, on the UI thread. Only one pass at a time.
    /// </summary>
    private void RefreshHeatmap()
    {
        if (!_heatmapDirty || _observer is not { } current || CurrentProject is not { } project) return;
        if (_heatmapBusy) return;
        _heatmapBusy = true;
        _heatmapDirty = false;
        bool rescan = _heatmapDiskDirty || _heatmapListing is null;
        _heatmapDiskDirty = false;
        long mine = _generation;
        KnowledgeGraph graph = _knowledgeBuilder.Snapshot();
        FileStatusIndex index = _lastIndex;
        IgnoreRules? rules = _lastRules;
        ProjectListing? known = _heatmapListing;
        Task.Run(() =>
        {
            ProjectListing listing = rescan ? ProjectScanner.Scan(project, rules ?? current.IgnoreRules(), index, ProjectScanner.DefaultMaxFiles) : known!;
            HeatTree tree = HeatTree.Build(project, listing, graph, index);
            OnUi(mine, () =>
            {
                _heatmapBusy = false;
                _heatmapListing = listing;
                _heatmap.SetTree(tree, graph);
                ApplyPendingHeatmap();
                if (_heatmapDirty) ScheduleHeatmapRefresh();
            });
        });
    }

    /// <summary><c>--heatmap-zoom</c> and <c>--heatmap-select</c>: paths (relative to the project or absolute) applied as soon as they exist in the map.</summary>
    public void SetHeatmapPending(string? zoom, string? select)
    {
        _pendingHeatmapZoom = zoom;
        _pendingHeatmapSelect = select;
        _heatmapDirty = true;
        if (_tabs.SelectedIndex == HeatmapTab) RefreshHeatmap();
    }

    private void ApplyPendingHeatmap()
    {
        if (_heatmap.Canvas.Tree is not { } tree) return;
        if (_pendingHeatmapZoom is { } zoom && tree.Find(ResolveInProject(zoom)) is { Directory: true } folder)
        {
            _heatmap.Canvas.ZoomTo(folder.Path);
            _pendingHeatmapZoom = null;
        }
        if (_pendingHeatmapSelect is { } select && tree.Find(ResolveInProject(select)) is { } node)
        {
            _heatmap.Canvas.Select(node.Path);
            _pendingHeatmapSelect = null;
        }
    }

    /// <summary>Shows "Knowledge" with that file selected.</summary>
    private void ShowInKnowledge(FilePath path)
    {
        SelectInKnowledge(path.Value);
        SwitchTab(KnowledgeTab);
    }

    /// <summary><c>--kg-select</c>: path (relative to the project or absolute) of a file in the knowledge graph.</summary>
    public void SelectInKnowledge(string path)
    {
        _pendingKnowledgeSelect = path;
        _knowledgeDirty = true;
        RefreshKnowledge();
    }

    private void ApplyPendingKnowledgeSelect()
    {
        if (_pendingKnowledgeSelect is null) return;
        FilePath absolute = ResolveInProject(_pendingKnowledgeSelect);
        if (!_knowledge.Canvas.Has(absolute)) return;
        _knowledge.Canvas.Select(absolute);
        _pendingKnowledgeSelect = null;
    }

    /// <summary>The changes git reports also feed the knowledge graph and the heatmap: the trace does not see what a console command writes.</summary>
    private void OnChangesForAnalysis(IReadOnlyList<ChangeEntry> changes)
    {
        _graph.SetChanges(changes);
        _knowledgeBuilder.SetChanges(changes);
        ScheduleKnowledgeRefresh();
        ScheduleHeatmapRefresh();
    }

    /// <summary>Repaints the graph at most every 250 ms: bursts of events are grouped.</summary>
    private void ScheduleGraphRefresh()
    {
        if (_graphRefreshPending) return;
        _graphRefreshPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _graphRefreshPending = false;
            _graph.SetGraph(_flow.Snapshot());
            ApplyPendingGraphSelect();
        }, TimeSpan.FromMilliseconds(250));
    }

    /// <summary><c>--graph-select</c>: a step (callId), <c>turn:&lt;n&gt;</c>, <c>last</c> or <c>last-error</c>.</summary>
    public void SelectInGraph(string spec)
    {
        _pendingGraphSelect = spec;
        ApplyPendingGraphSelect();
    }

    private void ApplyPendingGraphSelect()
    {
        if (_pendingGraphSelect is null) return;
        if (GraphSelection.Parse(_pendingGraphSelect, _graph.Canvas.Graph) is not { } selection) return;
        _graph.Canvas.Select(selection);
        _pendingGraphSelect = null;
    }

    private void Shutdown() => _observer?.Dispose();
}
