using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Git;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Text;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Integration;

/// <summary>
/// e2e of the tree model: an external process creates, modifies and deletes files while the project is observed with the
/// production timings; then what the tree would show (list, statuses and folders) is checked.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProjectTreeIT : TempDirTestBase
{
    /// <summary>An agent without traces: here what matters is the disk.</summary>
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

    private sealed class Recorder : IProjectObserverListener
    {
        private readonly object _lock = new();
        private readonly List<IReadOnlyList<ChangeEntry>> _changes = [];
        private readonly List<GitState> _states = [];
        private int _filesChanged;

        public int FilesChangedCount => Volatile.Read(ref _filesChanged);

        public List<GitState> States { get { lock (_lock) return [.. _states]; } }

        public IReadOnlyList<ChangeEntry> Latest()
        {
            lock (_lock) return _changes.Count == 0 ? [] : _changes[^1];
        }

        public void SessionChanged(string agent, FilePath trace)
        {
        }

        public void Events(IReadOnlyList<AgentEvent> events)
        {
        }

        public void Changes(IReadOnlyList<ChangeEntry> c)
        {
            lock (_lock) _changes.Add(c);
        }

        public void GitStateChanged(GitState state, string detail)
        {
            lock (_lock) _states.Add(state);
        }

        public void FilesChanged() => Interlocked.Increment(ref _filesChanged);
    }

    private static FileNode Node(IReadOnlyList<FileNode> nodes, string name) =>
        nodes.FirstOrDefault(n => n.Name == name) ?? throw new InvalidOperationException(name + " is not in " + string.Join(", ", nodes.Select(n => n.Name)));

    [Fact]
    public void TheTreeFollowsAnExternalCreationModificationAndDeletion()
    {
        Assert.SkipUnless(GitFixture.GitAvailable(), "This test needs git installed");
        GitFixture repo = GitFixture.Init(Dir.Combine("project"));
        repo.Write("base.txt", "base\n");
        repo.Write("docs/a.txt", "a\n");
        repo.Write("docs/b.txt", "b\n");
        repo.CommitAll("base");
        var r = new Recorder();

        using var observer = new ProjectObserver(repo.Dir, [new NoTracesAdapter()], GitCli.ForDirectory(repo.Dir),
            TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2), r);
        observer.Start();
        Eventually.Until("git did not become ready", () => r.States.Contains(GitState.Ready), 10_000, 25);

        long before = Stopwatch.GetTimestamp();
        ExternalProcess.Write(repo.Dir.Combine("fresh", "folder", "n.txt").Value, "new\n");
        ExternalProcess.Write(repo.Dir.Combine("docs", "a.txt").Value, "a modified\n");
        ExternalProcess.Delete(repo.Dir.Combine("docs", "b.txt").Value);

        Eventually.Until("the disk notice did not arrive within 3 s", () => r.FilesChangedCount > 0, 3_000, 25);
        Assert.True(Stopwatch.GetElapsedTime(before).TotalSeconds < 20);
        Eventually.Until("changes", () => r.Latest().Count == 3, 10_000, 25);

        // What the tree would show with those changes
        FileStatusIndex index = FileStatusIndex.Of(repo.Dir, r.Latest(), []);
        IgnoreRules rules = observer.IgnoreRules();
        IReadOnlyList<FileNode> top = DirectoryLister.Children(repo.Dir, rules, index);
        Assert.Equal(["docs", "fresh", "base.txt"], top.Select(n => n.Name));
        Assert.DoesNotContain(top, n => n.Name == ".git"); // .git is never shown

        NodeDecoration created = NodeDecoration.Of(Node(top, "fresh"), index);
        Assert.Equal(ChangeKind.Created, created.Kind);
        Assert.Equal(1, created.Count);

        NodeDecoration docs = NodeDecoration.Of(Node(top, "docs"), index);
        Assert.Equal(ChangeKind.Deleted, docs.Kind); // in a folder the strongest one rules
        Assert.Equal(2, docs.Count);
        Assert.Equal(Actor.External, docs.Actor); // without traces, everything is external

        IReadOnlyList<FileNode> inDocs = DirectoryLister.Children(repo.Dir.Combine("docs"), rules, index);
        Assert.Equal(["a.txt", "b.txt"], inDocs.Select(n => n.Name));
        Assert.Equal(ChangeKind.Modified, NodeDecoration.Of(Node(inDocs, "a.txt"), index).Kind);
        FileNode ghost = Node(inDocs, "b.txt");
        Assert.True(ghost.Ghost, "what was deleted stays in the tree as a ghost");
        NodeDecoration deleted = NodeDecoration.Of(ghost, index);
        Assert.Equal(ChangeKind.Deleted, deleted.Kind);
        Assert.True(deleted.Strike);

        // When the deleted file is put back, the ghost disappears
        ExternalProcess.Write(repo.Dir.Combine("docs", "b.txt").Value, "b\n");
        Eventually.Until("changes after restoring", () => r.Latest().Count == 2, 10_000, 25);
        FileStatusIndex after = FileStatusIndex.Of(repo.Dir, r.Latest(), []);
        Assert.False(Node(DirectoryLister.Children(repo.Dir.Combine("docs"), rules, after), "b.txt").Ghost);
    }

    [Fact]
    public void WithoutGitTheFixedIgnoreListAppliesAndDiskChangesStillArrive()
    {
        FilePath project = Dir.Combine("no-git");
        Directory.CreateDirectory(project.Combine("node_modules", "package").Value);
        File.WriteAllText(project.Combine("node_modules", "package", "index.js").Value, "x");
        Directory.CreateDirectory(project.Combine("src").Value);
        File.WriteAllText(project.Combine("src", "a.txt").Value, "a");
        var r = new Recorder();

        using var observer = new ProjectObserver(project, [new NoTracesAdapter()], GitCli.ForDirectory(project),
            TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2), r);
        observer.Start();
        Eventually.Until("git state", () => r.States.Count > 0, 10_000, 25);
        Assert.Equal(GitState.NoRepository, r.States[0]); // the temporary folder must not be inside a repository

        ExternalProcess.Write(project.Combine("src", "b.txt").Value, "b");
        Eventually.Until("without git the disk is watched too", () => r.FilesChangedCount > 0, 3_000, 25);

        IgnoreRules rules = observer.IgnoreRules();
        IReadOnlyList<FileNode> top = DirectoryLister.Children(project, rules, FileStatusIndex.Empty(project));
        Assert.Equal(["src"], top.Select(n => n.Name)); // node_modules is hidden by the fixed list
        Assert.Equal(["a.txt", "b.txt"],
            DirectoryLister.Children(project.Combine("src"), rules, FileStatusIndex.Empty(project)).Select(n => n.Name));
    }
}
