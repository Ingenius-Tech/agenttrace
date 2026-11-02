using AgentTrace.Core;
using AgentTrace.Git;
using AgentTrace.Presentation.Observation;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Git.GitChangeSource;

namespace AgentTrace.Presentation.Tests.Observation;

public sealed class ProjectObserverTest : TempDirTestBase
{
    public ProjectObserverTest() => Assert.SkipUnless(GitFixture.GitAvailable(), "These tests need git installed");

    /// <summary>An agent without traces: git observation does not depend on there being a session.</summary>
    private sealed class NoTracesAdapter : IAgentAdapter
    {
        public string Agent => "no-traces";

        public bool Owns(FilePath f) => false;

        public IReadOnlyList<FilePath> FindTraceFiles(FilePath dir) => [];

        public ILineParser NewParser(FilePath f) => new EmptyParser();

        private sealed class EmptyParser : ILineParser
        {
            public IReadOnlyList<AgentEvent> Parse(string line, long n) => [];
        }
    }

    /// <summary>
    /// An agent that stores its trace (<c>session.trace</c>) in the very folder where it was started, as Claude Code does with its
    /// project folder: each line is the path of a file it touched.
    /// </summary>
    private sealed class PerFolderAdapter : IAgentAdapter
    {
        public const string TraceName = "session.trace";

        public string Agent => "per-folder";

        public bool Owns(FilePath f) => f.FileName == TraceName;

        public IReadOnlyList<FilePath> FindTraceFiles(FilePath dir) =>
            File.Exists(dir.Combine(TraceName).Value) ? [dir.Combine(TraceName)] : [];

        public ILineParser NewParser(FilePath f) => new Parser(f);

        private sealed class Parser(FilePath file) : ILineParser
        {
            public IReadOnlyList<AgentEvent> Parse(string line, long n)
            {
                var header = new EventHeader(new SessionId(file.Parent!.Value.Value), "per-folder", null, DateTimeOffset.UtcNow, new SourceRef(file, n));
                return [new FileChanged(header, new FilePath(line.Trim()), ChangeKind.Modified, "call-" + n)];
            }
        }
    }

    private sealed class Recorder : IProjectObserverListener
    {
        private readonly object _lock = new();
        private readonly List<AgentEvent> _events = [];
        private readonly List<string> _sessions = [];

        public List<AgentEvent> Events_ { get { lock (_lock) return [.. _events]; } }

        public List<string> Sessions { get { lock (_lock) return [.. _sessions]; } }

        private readonly List<IReadOnlyList<ChangeEntry>> _changes = [];
        private readonly List<GitState> _states = [];
        private readonly List<string> _errors = [];
        private readonly List<string> _details = [];

        public List<GitState> States { get { lock (_lock) return [.. _states]; } }

        public List<string> Details { get { lock (_lock) return [.. _details]; } }

        public List<string> Errors { get { lock (_lock) return [.. _errors]; } }

        public int ChangeCount { get { lock (_lock) return _changes.Count; } }

        public IReadOnlyList<ChangeEntry>? Latest()
        {
            lock (_lock) return _changes.Count == 0 ? null : _changes[^1];
        }

        public void SessionChanged(string agent, FilePath trace)
        {
            lock (_lock) _sessions.Add(trace.Value);
        }

        public void Events(IReadOnlyList<AgentEvent> events)
        {
            lock (_lock) _events.AddRange(events);
        }

        public void Changes(IReadOnlyList<ChangeEntry> c)
        {
            lock (_lock) _changes.Add(c);
        }

        public void GitStateChanged(GitState state, string detail)
        {
            lock (_lock)
            {
                _states.Add(state);
                _details.Add(detail);
            }
        }

        public void Error(string message)
        {
            lock (_lock) _errors.Add(message);
        }
    }

    private static ProjectObserver Observer(FilePath dir, Recorder r) =>
        new(dir, [new NoTracesAdapter()], GitCli.ForDirectory(dir), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(150), r);

    private static List<string> Names(FilePath root, IReadOnlyList<ChangeEntry> entries) =>
        [.. entries.Select(e => e.Change.Path.RelativeTo(root).Replace('\\', '/'))];

    private GitFixture NewRepo()
    {
        GitFixture repo = GitFixture.Init(Dir.Combine("repo"));
        repo.Write("a.txt", "a\n");
        repo.CommitAll("base");
        return repo;
    }

    /// <summary>A folder without git with two repositories inside (<c>a</c> and <c>b</c>), each with a commit.</summary>
    private (FilePath Root, GitFixture A, GitFixture B) NewProjectOfTwoRepos()
    {
        FilePath root = Dir.Combine("project");
        GitFixture a = GitFixture.Init(root.Combine("a"));
        a.Write("a.txt", "a\n");
        a.CommitAll("base a");
        GitFixture b = GitFixture.Init(root.Combine("b"));
        b.Write("b.txt", "b\n");
        b.Write(".gitignore", "*.log\n");
        b.CommitAll("base b");
        return (root, a, b);
    }

    [Fact]
    public void AFolderWithoutGitHoldingTwoRepositoriesObservesBothAtOnce()
    {
        (FilePath root, GitFixture a, GitFixture b) = NewProjectOfTwoRepos();
        var r = new Recorder();
        using ProjectObserver o = Observer(root, r);
        o.Start();
        Eventually.Until("several repositories", () => r.States.Contains(GitState.MultipleRepositories), 8_000, 20);
        Assert.DoesNotContain(GitState.NoRepository, r.States);

        a.Write("a.txt", "a2\n");
        b.Write("new.txt", "n\n");
        Eventually.Until("one change in each repository", () => r.Latest() is { Count: 2 }, 8_000, 20);
        Assert.Equal(["a/a.txt", "b/new.txt"], Names(root, r.Latest()!));

        // Each file's diff comes from its own repository
        Assert.Equal("a\n", o.Versions(a.Dir.Combine("a.txt")).Before);
        Assert.Equal("a2\n", o.Versions(a.Dir.Combine("a.txt")).After);
        Assert.Equal("n\n", o.Versions(b.Dir.Combine("new.txt")).After);
        Assert.NotNull(o.Versions(root.Combine("loose.txt")).UnavailableReason); // outside the repositories there is no baseline
    }

    [Fact]
    public void AgentSessionsLaunchedInsideEachRepositoryAreFollowedToo()
    {
        (FilePath root, GitFixture a, GitFixture b) = NewProjectOfTwoRepos();
        FilePath inA = a.Dir.Combine("a.txt");
        FilePath inB = b.Dir.Combine("b.txt");
        // The agent was started in `a` and in `b`, not in the root: each trace is in its folder
        File.WriteAllText(a.Dir.Combine(PerFolderAdapter.TraceName).Value, inA.Value + "\n");
        File.WriteAllText(b.Dir.Combine(PerFolderAdapter.TraceName).Value, inB.Value + "\n");
        var r = new Recorder();
        using var o = new ProjectObserver(root, [new PerFolderAdapter()], GitCli.ForDirectory(root), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(150), r);
        o.Start();
        Eventually.Until("both sessions", () => r.Events_.OfType<FileChanged>().Select(e => e.Path).Distinct().Count() == 2, 15_000, 20);
        Assert.Equal(new[] { inA, inB }.OrderBy(p => p), r.Events_.OfType<FileChanged>().Select(e => e.Path).Distinct().OrderBy(p => p));

        // A single session notification (the first one to appear); the other only contributes events
        Assert.Single(r.Sessions);

        // A new session in the root is added too, without losing the previous ones
        FilePath loose = root.Combine("notes.md");
        File.WriteAllText(root.Combine(PerFolderAdapter.TraceName).Value, loose.Value + "\n");
        Eventually.Until("the root session", () => r.Events_.OfType<FileChanged>().Any(e => e.Path == loose), 15_000, 20);
    }

    [Fact]
    public void TheBaselineModesAndTheResetActOnEveryRepository()
    {
        (FilePath root, GitFixture a, GitFixture b) = NewProjectOfTwoRepos();
        a.Write("a.txt", "a-dirty\n");
        b.Write("b.txt", "b-dirty\n");
        var r = new Recorder();
        using ProjectObserver o = Observer(root, r);
        o.Start();
        Eventually.Until("several repositories", () => r.States.Contains(GitState.MultipleRepositories), 8_000, 20);
        Thread.Sleep(300);
        Assert.True(r.Latest() is null or { Count: 0 }, "what was dirty before does not count since the start");

        o.SetMode(BaselineMode.Head);
        Eventually.Until("against HEAD both appear", () => r.Latest() is { Count: 2 }, 8_000, 20);

        o.SetMode(BaselineMode.Session);
        a.Write("a.txt", "again\n");
        b.Write("b.txt", "again\n");
        Eventually.Until("two changes", () => r.Latest() is { Count: 2 }, 8_000, 20);
        o.ResetBaseline();
        Eventually.Until("empty list in both repositories", () => r.Latest() is { Count: 0 }, 8_000, 20);
    }

    [Fact]
    public void WhatEachRepositoryIgnoresIsHiddenInTheTree()
    {
        (FilePath root, _, GitFixture b) = NewProjectOfTwoRepos();
        b.Write("noise.log", "x\n");
        var r = new Recorder();
        using ProjectObserver o = Observer(root, r);
        o.Start();
        Eventually.Until("several repositories", () => r.States.Contains(GitState.MultipleRepositories), 8_000, 20);
        Ingest.IgnoreRules rules = o.IgnoreRules();
        Assert.True(rules.IsIgnored(b.Dir.Combine("noise.log")));
        Assert.False(rules.IsIgnored(b.Dir.Combine("b.txt")));
    }

    [Fact]
    public void PublishesRealChangesAsTheyHappen()
    {
        GitFixture repo = NewRepo();
        var r = new Recorder();
        using ProjectObserver o = Observer(repo.Dir, r);
        o.Start();
        Eventually.Until("repository ready", () => r.States.Contains(GitState.Ready), 8_000, 20);
        repo.Write("a.txt", "a2\n");
        repo.Write("new.txt", "n\n");
        Eventually.Until("two changes", () => r.Latest() is { Count: 2 }, 8_000, 20);
        Assert.Equal(["a.txt", "new.txt"], Names(repo.Dir, r.Latest()!));
        Assert.All(r.Latest()!, e => Assert.Equal(Confidence.None, e.Attribution.Confidence)); // without traces, the changes are external
        Assert.Equal("a\n", o.Versions(repo.Dir.Combine("a.txt")).Before);
    }

    [Fact]
    public void SwitchesBetweenSessionBaselineAndHead()
    {
        GitFixture repo = NewRepo();
        repo.Write("a.txt", "a-dirty\n");
        var r = new Recorder();
        using ProjectObserver o = Observer(repo.Dir, r);
        o.Start();
        Eventually.Until("repository ready", () => r.States.Contains(GitState.Ready), 8_000, 20);
        Thread.Sleep(300);
        Assert.True(r.Latest() is null or { Count: 0 }, "what was dirty before does not count since the start");

        o.SetMode(BaselineMode.Head);
        Eventually.Until("against HEAD the file appears", () => r.Latest() is { Count: 1 }, 8_000, 20);
        Assert.Equal(BaselineMode.Head, o.Mode);

        o.SetMode(BaselineMode.Session);
        Eventually.Until("back on the session there are no changes", () => r.Latest() is { Count: 0 }, 8_000, 20);
    }

    [Fact]
    public void ResetBaselineClearsTheList()
    {
        GitFixture repo = NewRepo();
        var r = new Recorder();
        using ProjectObserver o = Observer(repo.Dir, r);
        o.Start();
        Eventually.Until("repository ready", () => r.States.Contains(GitState.Ready), 8_000, 20);
        repo.Write("a.txt", "a2\n");
        Eventually.Until("one change", () => r.Latest() is { Count: 1 }, 8_000, 20);
        o.ResetBaseline();
        Eventually.Until("empty list after resetting", () => r.Latest() is { Count: 0 }, 8_000, 20);
    }

    [Fact]
    public void AFolderWithoutARepositoryIsReported()
    {
        FilePath dir = Dir.Combine("no-git");
        Directory.CreateDirectory(dir.Value);
        var r = new Recorder();
        using ProjectObserver o = Observer(dir, r);
        o.Start();
        Eventually.Until("state", () => r.States.Count > 0, 8_000, 20);
        Assert.Equal([GitState.NoRepository], r.States);
    }

    [Fact]
    public void AMissingGitProgramIsReportedAndTheObserverStillWorks()
    {
        FilePath dir = Dir.Combine("project");
        Directory.CreateDirectory(dir.Value);
        var r = new Recorder();
        var missing = new GitCli("git-program-that-does-not-exist", dir, TimeSpan.FromSeconds(2));
        using var o = new ProjectObserver(dir, [new NoTracesAdapter()], missing, TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(150), r);
        o.Start();
        Eventually.Until("state", () => r.States.Count > 0, 8_000, 20);
        Assert.Equal([GitState.NoGitProgram], r.States);
        Assert.Equal(UnavailableReason.NoRepository, o.Versions(dir.Combine("x")).UnavailableReason);
    }

    /// <summary>An agent whose lines are paths: each one produces a write (call, file change and result).</summary>
    private sealed class WritesAdapter(FilePath trace) : IAgentAdapter
    {
        public string Agent => "writes";

        public bool Owns(FilePath f) => f == trace;

        public IReadOnlyList<FilePath> FindTraceFiles(FilePath dir) => [trace];

        public ILineParser NewParser(FilePath f) => new Parser(f);

        private sealed class Parser(FilePath file) : ILineParser
        {
            public IReadOnlyList<AgentEvent> Parse(string line, long n)
            {
                var h = new EventHeader(new SessionId("s"), "writes", null, DateTimeOffset.UtcNow, new SourceRef(file, n));
                return
                [
                    new ToolCall(h, "c" + n, "Write", "{}"),
                    new FileChanged(h, new FilePath(line), ChangeKind.Created, "c" + n),
                    new ToolResult(h, "c" + n, false, "ok"),
                ];
            }
        }
    }

    [Fact]
    public void WithoutGitTheListComesFromTheTraceAndAGitInitSwitchesToRealChanges()
    {
        FilePath project = Dir.Combine("project");
        Directory.CreateDirectory(project.Value);
        FilePath inside = project.Combine("a.txt");
        FilePath outside = Dir.Combine("outside.txt");
        FilePath trace = Dir.Combine("trace.txt");
        File.WriteAllText(trace.Value, inside + "\n" + outside + "\n");
        var r = new Recorder();
        using var o = new ProjectObserver(project, [new WritesAdapter(trace)], GitCli.ForDirectory(project),
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(150), r);
        o.Start();
        Eventually.Until("state without a repository", () => r.States.Contains(GitState.NoRepository), 8_000, 20);
        Eventually.Until("list from the trace", () => r.Latest() is { Count: 1 }, 8_000, 20);
        Assert.Equal(["a.txt"], Names(project, r.Latest()!)); // what falls outside the project is not listed
        Assert.Equal(Confidence.Exact, r.Latest()![0].Attribution.Confidence);
        Assert.Equal(UnavailableReason.NoRepository, o.Versions(inside).UnavailableReason);
        Assert.False(Directory.Exists(project.Combine(".git").Value)); // nothing is created without confirmation

        o.InitRepository(false);
        Eventually.Until("repository ready", () => r.States.Contains(GitState.Ready), 8_000, 20);
        Assert.True(Directory.Exists(project.Combine(".git").Value));
        Eventually.Until("the list becomes git's (a.txt never existed on disk)", () => r.Latest() is { Count: 0 }, 8_000, 20);
    }

    /// <summary>An agent whose lines are paths that were read.</summary>
    private sealed class ReadsAdapter(FilePath trace) : IAgentAdapter
    {
        public string Agent => "reads";

        public bool Owns(FilePath f) => f == trace;

        public IReadOnlyList<FilePath> FindTraceFiles(FilePath dir) => [trace];

        public ILineParser NewParser(FilePath f) => new Parser(f);

        private sealed class Parser(FilePath file) : ILineParser
        {
            public IReadOnlyList<AgentEvent> Parse(string line, long n) =>
            [
                new FileRead(new EventHeader(new SessionId("s"), "reads", null, DateTimeOffset.UtcNow, new SourceRef(file, n)),
                    new FilePath(line), "c" + n),
            ];
        }
    }

    [Fact]
    public void CollectsTheFilesTheAgentReadInsideTheProjectOnly()
    {
        FilePath project = Dir.Combine("project");
        Directory.CreateDirectory(project.Value);
        FilePath inside = project.Combine("src", "A.java");
        FilePath outside = Dir.Combine("outside.txt");
        FilePath trace = Dir.Combine("reads.txt");
        File.WriteAllText(trace.Value, inside + "\n" + outside + "\n");
        var r = new Recorder();
        using var o = new ProjectObserver(project, [new ReadsAdapter(trace)], GitCli.ForDirectory(project),
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(150), r);
        o.Start();
        Eventually.Until("reads collected", () => o.Reads().Count > 0, 8_000, 20);
        Assert.Equal(new HashSet<FilePath> { inside }, o.Reads());
    }

    [Fact]
    public void DisposeStopsTheNotifications()
    {
        GitFixture repo = NewRepo();
        var r = new Recorder();
        ProjectObserver o = Observer(repo.Dir, r);
        o.Start();
        Eventually.Until("repository ready", () => r.States.Contains(GitState.Ready), 8_000, 20);
        o.Dispose();
        int before = r.ChangeCount;
        repo.Write("a.txt", "after\n");
        Thread.Sleep(400);
        Assert.Equal(before, r.ChangeCount);
    }

    [Fact]
    public void DoesNotModifyTheRepositoryWhileObserving()
    {
        GitFixture repo = NewRepo();
        string index = repo.Dir.Combine(".git", "index").Value;
        var r = new Recorder();
        using ProjectObserver o = Observer(repo.Dir, r);
        o.Start();
        Eventually.Until("repository ready", () => r.States.Contains(GitState.Ready), 8_000, 20);
        byte[] before = File.ReadAllBytes(index);
        DateTime mtime = File.GetLastWriteTimeUtc(index);
        repo.Write("a.txt", "a2\n");
        Eventually.Until("one change", () => r.Latest() is { Count: 1 }, 8_000, 20);
        o.Versions(repo.Dir.Combine("a.txt"));
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(index)));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(index));
    }

    [Fact]
    public void TheStatesCarryTheirTextInEnglish()
    {
        (FilePath root, _, _) = NewProjectOfTwoRepos();
        var several = new Recorder();
        using (ProjectObserver o = Observer(root, several)) o.Start();
        Assert.Equal(["2 repositories: a, b"], several.Details);

        FilePath bare = Dir.Combine("no-git");
        Directory.CreateDirectory(bare.Value);
        var none = new Recorder();
        using (ProjectObserver o = Observer(bare, none)) o.Start();
        Assert.Equal(["The folder is not a git repository: only the trace actions are shown, without diffs"], none.Details);

        var missing = new Recorder();
        var cli = new GitCli("git-program-that-does-not-exist", bare, TimeSpan.FromSeconds(2));
        using (var o = new ProjectObserver(bare, [new NoTracesAdapter()], cli, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(150), missing)) o.Start();
        Assert.Equal(["The git program was not found: only the trace actions are shown, without diffs. Install it or set AGENTTRACE_GIT=<path>"], missing.Details);

        var noAdapters = new Recorder();
        using (var o = new ProjectObserver(bare, [], GitCli.ForDirectory(bare), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(150), noAdapters)) o.Start();
        Assert.Equal(["No agent adapters available"], noAdapters.Errors);
    }

    [Fact]
    public void TheStatesCarryTheirTextInSpanishUnderEs()
    {
        using var culture = CultureScope.Spanish();
        (FilePath root, _, _) = NewProjectOfTwoRepos();
        var several = new Recorder();
        using (ProjectObserver o = Observer(root, several)) o.Start();
        Assert.Equal(["2 repositorios: a, b"], several.Details); // lang-ok

        FilePath bare = Dir.Combine("no-git");
        Directory.CreateDirectory(bare.Value);
        var none = new Recorder();
        using (ProjectObserver o = Observer(bare, none)) o.Start();
        Assert.Equal(["La carpeta no es un repositorio git: se muestran solo las acciones de la traza, sin diff"], none.Details); // lang-ok
    }
}
