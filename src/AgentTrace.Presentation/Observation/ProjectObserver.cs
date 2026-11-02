using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Git;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using static AgentTrace.Git.GitChangeSource;

namespace AgentTrace.Presentation.Observation;

public enum GitState
{
    /// <summary>Repository located and observed.</summary>
    Ready,

    /// <summary>The folder is not a repository but contains several in its subfolders: all of them are observed.</summary>
    MultipleRepositories,

    /// <summary>The git program cannot be found.</summary>
    NoGitProgram,

    /// <summary>The project is not inside any repository.</summary>
    NoRepository,

    /// <summary>Git failed (for example, a folder owned by another user).</summary>
    Error,
}

/// <summary>Whoever listens to a <see cref="ProjectObserver"/>. Notifications arrive from background threads.</summary>
public interface IProjectObserverListener
{
    void SessionChanged(string agent, FilePath sessionTrace);

    void Events(IReadOnlyList<AgentEvent> events);

    void Changes(IReadOnlyList<ChangeEntry> changes);

    void GitStateChanged(GitState state, string detail);

    void Error(string message)
    {
    }

    /// <summary>Something changed on the project's disk (no detail: whoever needs it queries the state). May arrive in bursts.</summary>
    void FilesChanged()
    {
    }
}

/// <summary>
/// Observes a project: follows the agent's active session live (traces), the real changes on disk (git) and
/// correlates one with the other. It does not depend on the UI; whoever uses it receives the notifications from background threads
/// and must pass them to its own thread if needed.
/// </summary>
public sealed class ProjectObserver : IDisposable
{
    private static readonly TimeSpan TracePoll = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TraceDiscovery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ChangeMaxWait = TimeSpan.FromSeconds(2);

    private readonly IReadOnlyList<IAgentAdapter> _adapters;
    private readonly GitCli _git;
    private readonly IProjectObserverListener _listener;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _maxWait;
    private readonly object _lock = new();
    private readonly ChangeCorrelator _correlator = new();

    private readonly List<TraceFollower> _traceFollowers = [];
    private readonly HashSet<FilePath> _followedSessions = [];

    /// <summary>The follower whose session is "the session": the first one to find one. Only it resets the analysis when the session changes.</summary>
    private TraceListener? _leader;
    private readonly List<RepoSlot> _slots = [];
    private LocalFileSystemWatcher? _watcher;
    private BaselineMode _mode = BaselineMode.Session;
    private IReadOnlyList<FileChange> _lastChanges = [];
    private IReadOnlyList<ChangeEntry>? _published;
    private bool _closed;

    /// <summary>An observed repository: its two baselines, the follower that queries its changes and the last thing it reported.</summary>
    private sealed class RepoSlot(GitRepository repo, GitChangeSource session, GitChangeSource head)
    {
        public GitRepository Repo { get; } = repo;

        public GitChangeSource Session { get; } = session;

        public GitChangeSource Head { get; } = head;

        public ChangeFollower? Follower { get; set; }

        public IReadOnlyList<FileChange> Changes { get; set; } = [];

        /// <summary>Folder observed inside that repository (the project, or its root if the project contains the repositories).</summary>
        public FilePath Scope { get; init; }
    }

    /// <summary>Files that the trace says were changed inside the project: they are the list when there is no git.</summary>
    private readonly Dictionary<FilePath, FileChange> _traceChanges = [];
    private readonly List<FilePath> _traceChangeOrder = [];
    private bool _traceOnly;

    /// <summary>Files that the agent read in the session, inside the project.</summary>
    private readonly HashSet<FilePath> _reads = [];

    public ProjectObserver(FilePath projectDir, IReadOnlyList<IAgentAdapter> adapters, IProjectObserverListener listener)
        : this(projectDir, adapters, GitCli.ForDirectory(projectDir), ChangeDebounce, ChangeMaxWait, listener)
    {
    }

    public ProjectObserver(FilePath projectDir, IReadOnlyList<IAgentAdapter> adapters, GitCli git, TimeSpan debounce,
        TimeSpan maxWait, IProjectObserverListener listener)
    {
        ProjectDir = projectDir;
        _adapters = adapters;
        _git = git;
        _debounce = debounce;
        _maxWait = maxWait;
        _listener = listener;
    }

    public FilePath ProjectDir { get; }

    public void Start()
    {
        StartWatcher();
        StartTraces();
        StartGit();
    }

    // ---- disk ----

    private void StartWatcher()
    {
        try
        {
            var fsWatcher = new LocalFileSystemWatcher(OnDiskChange);
            fsWatcher.StartWatching(ProjectDir);
            lock (_lock)
            {
                if (_closed)
                {
                    fsWatcher.Dispose();
                    return;
                }
                _watcher = fsWatcher;
            }
        }
        catch (IOException e)
        {
            _listener.Error(string.Format(CultureInfo.CurrentCulture, ShellStrings.ObserverWatchFailedFormat, e.Message));
        }
    }

    private void OnDiskChange()
    {
        TriggerRefresh();
        _listener.FilesChanged();
    }

    // ---- traces ----

    private void StartTraces()
    {
        if (!StartTraceFollower(ProjectDir)) _listener.Error(ShellStrings.ObserverNoAdapters);
    }

    /// <summary>
    /// An agent started inside a subrepository writes its trace under that subrepository's folder, not under the project's:
    /// with several repositories the sessions of each one are followed too, and their events are added to the rest.
    /// </summary>
    private void StartRepositoryTraces(IEnumerable<FilePath> repositoryRoots)
    {
        foreach (FilePath root in repositoryRoots.Where(r => r != ProjectDir)) StartTraceFollower(root);
    }

    private bool StartTraceFollower(FilePath dir)
    {
        IAgentAdapter? adapter = ChooseAdapter(dir);
        if (adapter is null) return false;
        // Two folders that point to the same session (e.g. a link) are not followed twice
        if (adapter.FindTraceFiles(dir) is [var first, ..])
        {
            lock (_lock)
            {
                if (!_followedSessions.Add(first)) return true;
            }
        }
        var follower = new TraceFollower(adapter, dir, TracePoll, TraceDiscovery, new TraceListener(this, adapter));
        lock (_lock)
        {
            if (_closed) return true;
            _traceFollowers.Add(follower);
        }
        follower.Start();
        return true;
    }

    private sealed class TraceListener(ProjectObserver owner, IAgentAdapter adapter) : ITraceFollowerListener
    {
        public void SessionChanged(FilePath sessionTrace)
        {
            lock (owner._lock)
            {
                // With several followers, only the first to find a session sets "the session"; the rest only contribute events
                owner._leader ??= this;
                if (!ReferenceEquals(owner._leader, this)) return;

                owner._correlator.Reset();
                owner._traceChanges.Clear();
                owner._traceChangeOrder.Clear();
                owner._reads.Clear();
                if (owner._traceOnly) owner._lastChanges = [];
                owner.ResetSessionBaseline();
            }
            owner._listener.SessionChanged(adapter.Agent, sessionTrace);
            owner.Republish();
        }

        public void Events(IReadOnlyList<AgentEvent> events)
        {
            lock (owner._lock)
            {
                foreach (AgentEvent e in events) owner._correlator.Accept(e);
                owner.CollectTraceChanges(events);
                owner.CollectReads(events);
                if (owner._traceOnly) owner._lastChanges = owner.SortedTraceChanges();
            }
            owner._listener.Events(events);
            owner.Republish();
        }

        public void Error(Exception error) => owner._listener.Error(string.Format(CultureInfo.CurrentCulture, ShellStrings.ObserverTraceReadErrorFormat, error.Message));
    }

    /// <summary>With several agents, the one with the most recent session; if none has one yet, the first.</summary>
    private IAgentAdapter? ChooseAdapter(FilePath dir) =>
        _adapters
            .Where(a => a.FindTraceFiles(dir).Count > 0)
            .MaxBy(a => File.GetLastWriteTimeUtc(a.FindTraceFiles(dir)[0].Value))
        ?? _adapters.FirstOrDefault();

    // ---- git ----

    private void StartGit()
    {
        if (!GitRepository.IsAvailable(_git))
        {
            EnterTraceOnly();
            _listener.GitStateChanged(GitState.NoGitProgram, ShellStrings.ObserverNoGitProgram);
            return;
        }
        GitRepository? found;
        try
        {
            found = GitRepository.Locate(_git, ProjectDir);
        }
        catch (IOException e)
        {
            EnterTraceOnly();
            _listener.GitStateChanged(GitState.Error, ReasonText.ErrorMessage(e));
            return;
        }
        if (found is null)
        {
            // A folder without git that contains several repositories is a project made of them
            DiscoveredRepositories inside = RepositoryDiscovery.Find(_git, ProjectDir);
            foreach (string problem in inside.Problems) _listener.Error(string.Format(CultureInfo.CurrentCulture, ShellStrings.ObserverRepositorySkippedFormat, problem));
            if (inside.Repositories.Count == 0)
            {
                EnterTraceOnly();
                _listener.GitStateChanged(GitState.NoRepository, ShellStrings.ObserverNotARepository);
                return;
            }
            AttachAll(inside.Repositories.Select(r => (r, r.Root)).ToList(), GitState.MultipleRepositories,
                string.Format(CultureInfo.CurrentCulture, Plural.Pick(inside.Repositories.Count, ShellStrings.ObserverRepositories_One, ShellStrings.ObserverRepositories_Other),
                    LocalNumbers.Grouped(inside.Repositories.Count), string.Join(", ", inside.Repositories.Select(r => r.Root.RelativeTo(ProjectDir)))));
            return;
        }
        AttachAll([(found, ProjectDir)], GitState.Ready, found.Root.ToString());
    }

    private void AttachAll(IReadOnlyList<(GitRepository Repo, FilePath Scope)> repos, GitState state, string detail)
    {
        try
        {
            Attach(repos);
        }
        catch (IOException e)
        {
            EnterTraceOnly();
            _listener.GitStateChanged(GitState.Error, ReasonText.ErrorMessage(e));
            return;
        }
        _listener.GitStateChanged(state, detail);
    }

    private void Attach(IReadOnlyList<(GitRepository Repo, FilePath Scope)> repos)
    {
        List<RepoSlot> slots = [];
        foreach ((GitRepository repo, FilePath scope) in repos)
        {
            slots.Add(new RepoSlot(repo, new GitChangeSource(repo, scope, BaselineMode.Session), new GitChangeSource(repo, scope, BaselineMode.Head)) { Scope = scope });
        }
        LocalFileSystemWatcher? fsWatcher;
        lock (_lock) fsWatcher = _watcher;
        // Commits and branch changes are only visible in git's data directory
        foreach (RepoSlot slot in slots) fsWatcher?.Watch(slot.Repo.GitDir);
        lock (_lock)
        {
            if (_closed) return;
            _slots.Clear();
            _slots.AddRange(slots);
            _traceOnly = false;
            _lastChanges = [];
            _published = null;
            StartFollowers();
        }
        // Repositories inside the folder (not the folder inside one): their agent sessions are in their own folders
        StartRepositoryTraces(repos.Where(r => r.Scope != ProjectDir).Select(r => r.Repo.Root));
    }

    /// <summary>Without git, the list comes from what the trace reports.</summary>
    private void EnterTraceOnly()
    {
        lock (_lock)
        {
            _traceOnly = true;
            _lastChanges = SortedTraceChanges();
        }
        Republish();
    }

    /// <summary>Must be called with <c>_lock</c> held.</summary>
    private void CollectTraceChanges(IReadOnlyList<AgentEvent> events)
    {
        foreach (AgentEvent evt in events)
        {
            if (evt is not AgentEvent.FileChanged changed || !changed.Path.IsUnder(ProjectDir)) continue;
            if (!_traceChanges.ContainsKey(changed.Path)) _traceChangeOrder.Add(changed.Path);
            _traceChanges[changed.Path] = new FileChange(changed.Path, changed.Kind, changed.Header.Timestamp);
        }
    }

    /// <summary>Must be called with <c>_lock</c> held.</summary>
    private void CollectReads(IReadOnlyList<AgentEvent> events)
    {
        foreach (AgentEvent evt in events)
        {
            if (evt is AgentEvent.FileRead read && read.Path.IsUnder(ProjectDir)) _reads.Add(read.Path);
        }
    }

    /// <summary>Must be called with <c>_lock</c> held.</summary>
    private IReadOnlyList<FileChange> SortedTraceChanges() => [.. _traceChanges.Values.OrderBy(c => c.Path)];

    /// <summary>One follower per repository. Must be called with <c>_lock</c> held.</summary>
    private void StartFollowers()
    {
        foreach (RepoSlot slot in _slots)
        {
            IChangeSource source = _mode == BaselineMode.Session ? slot.Session : slot.Head;
            slot.Follower = new ChangeFollower(source, _debounce, _maxWait, new ChangeListener(this, slot));
            slot.Follower.Start();
        }
    }

    /// <summary>The published list is that of all the repositories together, by path. Must be called with <c>_lock</c> held.</summary>
    private void MergeChanges() =>
        _lastChanges = _slots.Count == 1 ? _slots[0].Changes : [.. _slots.SelectMany(s => s.Changes).OrderBy(c => c.Path)];

    private sealed class ChangeListener(ProjectObserver owner, RepoSlot slot) : IChangeFollowerListener
    {
        public void ChangesUpdated(IReadOnlyList<FileChange> changes)
        {
            lock (owner._lock)
            {
                slot.Changes = changes;
                owner.MergeChanges();
            }
            owner.Republish();
        }

        public void Error(Exception error) => owner._listener.Error(string.Format(CultureInfo.CurrentCulture, ShellStrings.ObserverGitErrorFormat, ReasonText.ErrorMessage(error)));
    }

    private void TriggerRefresh()
    {
        List<ChangeFollower> followers;
        lock (_lock) followers = [.. _slots.Select(s => s.Follower).OfType<ChangeFollower>()];
        foreach (ChangeFollower follower in followers) follower.Trigger();
    }

    /// <summary>Must be called with <c>_lock</c> held.</summary>
    private void ResetSessionBaseline()
    {
        foreach (RepoSlot slot in _slots)
        {
            try
            {
                slot.Session.ResetBaseline();
            }
            catch (IOException e)
            {
                _listener.Error(string.Format(CultureInfo.CurrentCulture, ShellStrings.ObserverBaselineResetFailedFormat, slot.Repo.Root.FileName, ReasonText.ErrorMessage(e)));
            }
            slot.Follower?.Trigger();
        }
    }

    /// <summary>Recomputes the attributions with the latest from the trace and notifies only if something changed.</summary>
    private void Republish()
    {
        List<ChangeEntry> entries;
        lock (_lock)
        {
            entries = [.. _lastChanges.Select(c => new ChangeEntry(c, _correlator.Attribute(c)))];
            if (_published is not null && entries.SequenceEqual(_published)) return;
            _published = entries;
        }
        _listener.Changes(entries);
    }

    // ---- user actions ----

    public BaselineMode Mode
    {
        get
        {
            lock (_lock) return _mode;
        }
    }

    /// <summary>Switches between "since the start of the observation" and "against HEAD".</summary>
    public void SetMode(BaselineMode newMode)
    {
        List<ChangeFollower> old;
        lock (_lock)
        {
            if (_closed || newMode == _mode || _slots.Count == 0) return;
            _mode = newMode;
            old = [.. _slots.Select(s => s.Follower).OfType<ChangeFollower>()];
            foreach (RepoSlot slot in _slots)
            {
                slot.Follower = null;
                slot.Changes = [];
            }
            _published = null;
            _lastChanges = [];
        }
        foreach (ChangeFollower follower in old) follower.Dispose();
        lock (_lock)
        {
            if (!_closed) StartFollowers();
        }
    }

    /// <summary>
    /// Creates a git repository in the project folder (with an initial commit if requested) and starts observing it.
    /// Must only be called after the user's explicit confirmation: it modifies the project folder.
    /// </summary>
    public void InitRepository(bool initialCommit)
    {
        GitRepository.Init(_git, ProjectDir, initialCommit);
        StartGit();
    }

    /// <summary>Takes the current state as the new baseline of the session.</summary>
    public void ResetBaseline()
    {
        lock (_lock) ResetSessionBaseline();
    }

    /// <summary>The two versions of a file according to the chosen baseline.</summary>
    public FileVersions Versions(FilePath file)
    {
        GitChangeSource? source;
        int repos;
        lock (_lock)
        {
            // The deepest repository that contains the file (if there are repositories inside others, the inner one wins)
            RepoSlot? slot = _slots.Where(s => file.IsUnder(s.Repo.Root)).MaxBy(s => s.Repo.Root.Value.Length);
            source = slot is null ? null : _mode == BaselineMode.Session ? slot.Session : slot.Head;
            repos = _slots.Count;
        }
        if (source is null)
        {
            return FileVersions.Unavailable(file, repos == 0 ? UnavailableReason.NoRepository : UnavailableReason.OutsideObservedRepositories);
        }
        return source.Versions(file);
    }

    /// <summary>What is hidden in the tree. With git it queries git, so it must not be called from the UI thread.</summary>
    public IgnoreRules IgnoreRules()
    {
        List<RepoSlot> slots;
        lock (_lock) slots = [.. _slots];
        if (slots.Count == 0) return Ingest.IgnoreRules.WithoutGit(ProjectDir);
        try
        {
            return Ingest.IgnoreRules.WithGit(ProjectDir, slots.SelectMany(s => s.Repo.IgnoredPaths(s.Scope)).ToList());
        }
        catch (IOException)
        {
            return Ingest.IgnoreRules.WithoutGit(ProjectDir);
        }
    }

    /// <summary>Project files that the agent read in the session.</summary>
    public IReadOnlySet<FilePath> Reads()
    {
        lock (_lock) return new HashSet<FilePath>(_reads);
    }

    public void Dispose()
    {
        List<TraceFollower> traces;
        List<ChangeFollower> followers;
        LocalFileSystemWatcher? fsWatcher;
        lock (_lock)
        {
            _closed = true;
            traces = [.. _traceFollowers];
            followers = [.. _slots.Select(s => s.Follower).OfType<ChangeFollower>()];
            fsWatcher = _watcher;
            _traceFollowers.Clear();
            foreach (RepoSlot slot in _slots) slot.Follower = null;
            _watcher = null;
        }
        foreach (TraceFollower trace in traces) trace.Dispose();
        foreach (ChangeFollower follower in followers) follower.Dispose();
        fsWatcher?.Dispose();
    }
}
