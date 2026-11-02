using System.Text;
using AgentTrace.Core;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Git.GitChangeSource;

namespace AgentTrace.Git.Tests;

public sealed class GitChangeSourceTest : GitTestBase
{
    private readonly GitFixture _repo;

    public GitChangeSourceTest()
    {
        _repo = GitFixture.Init(Tmp.Combine("repo"));
    }

    private GitChangeSource Source(FilePath projectDir, BaselineMode mode)
    {
        GitRepository r = GitRepository.Locate(GitCli.ForDirectory(Tmp), projectDir)!;
        return new GitChangeSource(r, projectDir, mode);
    }

    private GitChangeSource Session() => Source(_repo.Dir, BaselineMode.Session);

    private static Dictionary<string, ChangeKind> Kinds(FilePath root, IReadOnlyList<FileChange> changes) =>
        changes.ToDictionary(c => c.Path.RelativeTo(root).Replace('\\', '/'), c => c.Kind);

    [Fact]
    public void ReportsModifiedNewAndDeletedFilesSortedByPath()
    {
        _repo.Write("a.txt", "a\n");
        _repo.Write("b.txt", "b\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        Assert.Empty(src.Changes());

        _repo.Write("a.txt", "a2\n");
        _repo.Write("c.txt", "new\n");
        File.Delete(_repo.Dir.Combine("b.txt").Value);

        IReadOnlyList<FileChange> changes = src.Changes();
        Assert.Equal(new Dictionary<string, ChangeKind>
        {
            ["a.txt"] = ChangeKind.Modified,
            ["b.txt"] = ChangeKind.Deleted,
            ["c.txt"] = ChangeKind.Created,
        }, Kinds(_repo.Dir, changes));
        Assert.Equal(["a.txt", "b.txt", "c.txt"], changes.Select(c => c.Path.RelativeTo(_repo.Dir)).ToList());
        Assert.NotNull(changes[0].ModifiedAt);
        Assert.Null(changes[1].ModifiedAt);
    }

    [Fact]
    public void FilesAlreadyDirtyAtStartAppearOnlyWhenTheyChangeAgain()
    {
        _repo.Write("a.txt", "a\n");
        _repo.Write("b.txt", "b\n");
        _repo.CommitAll("base");
        _repo.Write("a.txt", "a-dirty\n");
        _repo.Write("u.txt", "untracked\n");
        GitChangeSource src = Session();
        Assert.Empty(src.Changes());

        _repo.Write("a.txt", "a-dirty-and-more\n");
        IReadOnlyList<FileChange> changes = src.Changes();
        Assert.Equal(new Dictionary<string, ChangeKind> { ["a.txt"] = ChangeKind.Modified }, Kinds(_repo.Dir, changes));
        FileVersions v = src.Versions(_repo.Dir.Combine("a.txt"));
        Assert.Equal("a-dirty\n", v.Before);
        Assert.Equal("a-dirty-and-more\n", v.After);

        // HEAD mode does see everything that differs from the last commit
        GitChangeSource head = Source(_repo.Dir, BaselineMode.Head);
        Assert.Equal(new Dictionary<string, ChangeKind>
        {
            ["a.txt"] = ChangeKind.Modified,
            ["u.txt"] = ChangeKind.Created,
        }, Kinds(_repo.Dir, head.Changes()));
    }

    [Fact]
    public void ACommitMadeDuringTheSessionDoesNotHideTheChange()
    {
        _repo.Write("a.txt", "a\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        GitChangeSource head = Source(_repo.Dir, BaselineMode.Head);

        _repo.Write("a.txt", "a-agent\n");
        _repo.CommitAll("the agent commits");

        Assert.Equal(new Dictionary<string, ChangeKind> { ["a.txt"] = ChangeKind.Modified }, Kinds(_repo.Dir, src.Changes()));
        Assert.Equal("a\n", src.Versions(_repo.Dir.Combine("a.txt")).Before);
        Assert.Empty(head.Changes());
    }

    [Fact]
    public void RepositoryWithoutCommitsComparesAgainstTheEmptyTree()
    {
        GitChangeSource src = Session();
        _repo.Write("new.txt", "hello\n");
        IReadOnlyList<FileChange> changes = src.Changes();
        Assert.Equal(new Dictionary<string, ChangeKind> { ["new.txt"] = ChangeKind.Created }, Kinds(_repo.Dir, changes));
        FileVersions v = src.Versions(_repo.Dir.Combine("new.txt"));
        Assert.Null(v.Before);
        Assert.Equal("hello\n", v.After);
    }

    [Fact]
    public void HandlesSpacesAccentsAndEmojiInNames()
    {
        _repo.Write("base.txt", "x\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        _repo.Write("con espacio/añadido ñ.txt", "1\n"); // lang-ok
        _repo.Write("emoji-😀.txt", "2\n");
        Assert.Equal(new Dictionary<string, ChangeKind>
        {
            ["con espacio/añadido ñ.txt"] = ChangeKind.Created, // lang-ok
            ["emoji-😀.txt"] = ChangeKind.Created,
        }, Kinds(_repo.Dir, src.Changes()));
    }

    [Fact]
    public void IgnoredFilesAreNotListed()
    {
        _repo.Write(".gitignore", "*.log\nbuild/\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        _repo.Write("x.log", "noise\n");
        _repo.Write("build/output.txt", "noise\n");
        _repo.Write("real.txt", "data\n");
        Assert.Equal(new Dictionary<string, ChangeKind> { ["real.txt"] = ChangeKind.Created }, Kinds(_repo.Dir, src.Changes()));
    }

    [Fact]
    public void AProjectInASubdirectoryOnlySeesItsOwnChanges()
    {
        _repo.Write("sub/a.txt", "a\n");
        _repo.Write("other/b.txt", "b\n");
        _repo.CommitAll("base");
        FilePath project = _repo.Dir.Combine("sub");
        GitChangeSource src = Source(project, BaselineMode.Session);
        _repo.Write("sub/a.txt", "a2\n");
        _repo.Write("other/b.txt", "b2\n");
        Assert.Equal(new Dictionary<string, ChangeKind> { ["sub/a.txt"] = ChangeKind.Modified }, Kinds(_repo.Dir, src.Changes()));
    }

    [Fact]
    public void ARenameIsADeletionPlusACreation()
    {
        _repo.Write("old.txt", "content\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        File.Move(_repo.Dir.Combine("old.txt").Value, _repo.Dir.Combine("new.txt").Value);
        Assert.Equal(new Dictionary<string, ChangeKind>
        {
            ["old.txt"] = ChangeKind.Deleted,
            ["new.txt"] = ChangeKind.Created,
        }, Kinds(_repo.Dir, src.Changes()));
    }

    [Fact]
    public void AFileDirtyAtStartThenDeletedIsDeletedAndOneDeletedAtStartStaysHidden()
    {
        _repo.Write("a.txt", "a\n");
        _repo.Write("b.txt", "b\n");
        _repo.CommitAll("base");
        _repo.Write("a.txt", "a-dirty\n");
        File.Delete(_repo.Dir.Combine("b.txt").Value);
        GitChangeSource src = Session();
        Assert.Empty(src.Changes());
        File.Delete(_repo.Dir.Combine("a.txt").Value);
        Assert.Equal(new Dictionary<string, ChangeKind> { ["a.txt"] = ChangeKind.Deleted }, Kinds(_repo.Dir, src.Changes()));
    }

    [Fact]
    public void ResetBaselineStartsFromTheCurrentState()
    {
        _repo.Write("a.txt", "a\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        _repo.Write("a.txt", "a2\n");
        Assert.Single(src.Changes());
        src.ResetBaseline();
        Assert.Empty(src.Changes());
        _repo.Write("a.txt", "a3\n");
        Assert.Equal("a2\n", src.Versions(_repo.Dir.Combine("a.txt")).Before);
    }

    [Fact]
    public void BinaryAndHugeFilesHaveNoText()
    {
        _repo.Write("base.txt", "x\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        FilePath bin = _repo.WriteBytes("image.bin", [1, 2, 0, 3]);
        FilePath big = _repo.Write("big.txt", string.Concat(Enumerable.Repeat("lines\n", 400_000)));
        Assert.Equal(UnavailableReason.Binary, src.Versions(bin).UnavailableReason);
        Assert.Equal(UnavailableReason.TooLarge, src.Versions(big).UnavailableReason);
        Assert.Equal(2, src.Changes().Count);
    }

    [Fact]
    public void LineEndingConversionByGitIsNotReportedAsAChange()
    {
        _repo.Run("config", "core.autocrlf", "true");
        _repo.Write("a.txt", "one\ntwo\n");
        _repo.CommitAll("base");
        GitChangeSource src = Session();
        File.WriteAllBytes(_repo.Dir.Combine("a.txt").Value, Encoding.UTF8.GetBytes("one\r\ntwo\r\n"));
        Assert.Empty(src.Changes());
    }

    [Fact]
    public void WorksInsideAWorktree()
    {
        _repo.Write("a.txt", "a\n");
        _repo.CommitAll("base");
        FilePath worktree = Tmp.Combine("work");
        _repo.Run("worktree", "add", worktree.Value, "-b", "branch");
        GitChangeSource src = Source(worktree, BaselineMode.Session);
        File.WriteAllText(worktree.Combine("a.txt").Value, "a-in-worktree\n");
        Assert.Equal(new Dictionary<string, ChangeKind> { ["a.txt"] = ChangeKind.Modified }, Kinds(worktree, src.Changes()));
        Assert.Equal("a\n", src.Versions(worktree.Combine("a.txt")).Before);
    }

    [Fact]
    public void ReadingNeverModifiesTheRepository()
    {
        _repo.Write("a.txt", "a\n");
        _repo.CommitAll("base");
        FilePath index = _repo.Dir.Combine(".git/index");
        byte[] before = File.ReadAllBytes(index.Value);
        DateTime mtime = File.GetLastWriteTimeUtc(index.Value);
        GitChangeSource src = Session();
        _repo.Write("a.txt", "a2\n");
        _repo.Write("new.txt", "n\n");
        byte[] afterEdits = File.ReadAllBytes(index.Value);
        src.Changes();
        src.Versions(_repo.Dir.Combine("a.txt"));
        src.Changes();
        Assert.Equal(afterEdits, File.ReadAllBytes(index.Value));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(index.Value));
        Assert.NotNull(before);
    }
}
