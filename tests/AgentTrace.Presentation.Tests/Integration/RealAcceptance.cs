using AgentTrace.Adapters.ClaudeCode;
using AgentTrace.Core;
using AgentTrace.Git;
using AgentTrace.Presentation.Observation;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Integration;

/// <summary>
/// Base of the acceptance tests with REAL changes made by Claude Code in this very project: real git and the real trace
/// of the session. Each one is enabled with its environment variable and skipped without it (see CLAUDE.md, "Commands").
/// </summary>
internal sealed class RealAcceptance : IProjectObserverListener, IDisposable
{
    private readonly object _lock = new();
    private readonly List<IReadOnlyList<ChangeEntry>> _updates = [];
    private readonly List<GitState> _states = [];

    public ProjectObserver Observer { get; }

    public FilePath Project { get; }

    private RealAcceptance(FilePath project)
    {
        Project = project;
        Observer = new ProjectObserver(project, [new ClaudeCodeAdapter()], this);
    }

    /// <summary>Skips the test without the variable or without git; otherwise starts the observation against HEAD and waits for git.</summary>
    public static (RealAcceptance Acceptance, string Spec) StartOrSkip(string variable)
    {
        string? spec = Environment.GetEnvironmentVariable(variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(spec), "Without " + variable + ": test skipped");
        Assert.SkipUnless(GitFixture.GitAvailable(), "This test needs git installed");
        var acceptance = new RealAcceptance(TestPaths.RepoRoot());
        acceptance.Observer.Start();
        Eventually.Until("git state", () => acceptance.States.Contains(GitState.Ready), 15_000, 50);
        // Against HEAD: what the agent had already changed before the observation was opened also counts
        acceptance.Observer.SetMode(GitChangeSource.BaselineMode.Head);
        return (acceptance, spec!);
    }

    public List<GitState> States { get { lock (_lock) return [.. _states]; } }

    public IReadOnlyList<ChangeEntry> Latest()
    {
        lock (_lock) return _updates.Count == 0 ? [] : _updates[^1];
    }

    public ChangeEntry? Find(FilePath file) => Latest().FirstOrDefault(e => e.Change.Path == file);

    public void SessionChanged(string agent, FilePath sessionTrace)
    {
    }

    public void Events(IReadOnlyList<AgentEvent> events)
    {
    }

    public void Changes(IReadOnlyList<ChangeEntry> changes)
    {
        lock (_lock) _updates.Add(changes);
    }

    public void GitStateChanged(GitState state, string detail)
    {
        lock (_lock) _states.Add(state);
    }

    public void Dispose() => Observer.Dispose();
}
