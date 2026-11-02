using AgentTrace.Core;
using AgentTrace.Testing;

namespace AgentTrace.Git.Tests;

public sealed class GitRepositoryTest : GitTestBase
{
    private static void AssertSameSet(IEnumerable<FilePath> expected, IReadOnlySet<FilePath> actual)
    {
        Assert.Equal(expected.OrderBy(p => p).ToList(), actual.OrderBy(p => p).ToList());
    }

    [Fact]
    public void DetectsAMissingGitProgram()
    {
        GitCli missing = new("nonexistent-git-program", Tmp, TimeSpan.FromSeconds(5));
        Assert.False(GitRepository.IsAvailable(missing));
        Assert.True(GitRepository.IsAvailable(GitCli.ForDirectory(Tmp)));
    }

    [Fact]
    public void AFolderOutsideAnyRepositoryHasNone()
    {
        Assert.Null(GitRepository.Locate(GitCli.ForDirectory(Tmp), Tmp));
    }

    [Fact]
    public void LocatesTheRepositoryFromItsRootAndFromASubdirectory()
    {
        GitFixture repo = GitFixture.Init(Tmp.Combine("repo"));
        FilePath sub = new(Directory.CreateDirectory(repo.Dir.Combine("a", "b").Value).FullName);
        GitRepository fromRoot = GitRepository.Locate(GitCli.ForDirectory(Tmp), repo.Dir)!;
        GitRepository fromSub = GitRepository.Locate(GitCli.ForDirectory(Tmp), sub)!;
        Assert.Equal(repo.Dir, fromRoot.Root);
        Assert.Equal(fromRoot.Root, fromSub.Root);
        Assert.Equal(".git", fromRoot.GitDir.FileName);
    }

    [Fact]
    public void LocatesAWorktreeThroughItsGitFile()
    {
        GitFixture repo = GitFixture.Init(Tmp.Combine("main"));
        repo.Write("a.txt", "a\n");
        repo.CommitAll("one");
        FilePath worktree = Tmp.Combine("work");
        repo.Run("worktree", "add", worktree.Value, "-b", "branch");
        Assert.True(File.Exists(worktree.Combine(".git").Value), "a worktree's .git is a file");
        GitRepository found = GitRepository.Locate(GitCli.ForDirectory(Tmp), worktree)!;
        Assert.Equal(worktree, found.Root);
        Assert.True(Directory.Exists(found.GitDir.Value), "the data directory must resolve: " + found.GitDir);
    }

    [Fact]
    public void HeadIsNullUntilThereIsACommit()
    {
        GitFixture repo = GitFixture.Init(Tmp.Combine("repo"));
        GitRepository r = GitRepository.Locate(GitCli.ForDirectory(Tmp), repo.Dir)!;
        Assert.Null(r.HeadSha());
        repo.Write("a.txt", "a\n");
        string sha = repo.CommitAll("one");
        Assert.Equal(sha, r.HeadSha());
        Assert.Matches("^[0-9a-f]{40}$", sha);
    }

    [Fact]
    public void InitCreatesARepositoryWithoutTouchingFilesAndCanCommitWhenIdentityExists()
    {
        FilePath dir = new(Directory.CreateDirectory(Tmp.Combine("fresh").Value).FullName);
        File.WriteAllText(dir.Combine("a.txt").Value, "hello\n");
        GitCli cli = GitCli.ForDirectory(dir);
        GitRepository.Init(cli, dir, false);
        GitRepository repo = GitRepository.Locate(cli, dir)!;
        Assert.Null(repo.HeadSha());
        Assert.Equal("hello\n", File.ReadAllText(dir.Combine("a.txt").Value));

        bool identity = cli.Exec("config", "user.name").ExitCode == 0 && cli.Exec("config", "user.email").ExitCode == 0;
        if (identity)
        {
            GitRepository.Init(cli, dir, true);
            Assert.NotNull(repo.HeadSha());
        }
        else
        {
            GitException e = Assert.Throws<GitException>(() => GitRepository.Init(cli, dir, true));
            Assert.Contains("git", e.Message);
        }
    }

    [Fact]
    public void LargeOutputDoesNotBlockTheProcess()
    {
        GitFixture repo = GitFixture.Init(Tmp.Combine("large"));
        for (int i = 0; i < 3000; i++) repo.Write("folder-with-a-rather-long-name/file-with-a-fairly-long-name-indeed-" + i + ".txt", "x");
        repo.Run("add", "-A");
        GitCli.Result r = repo.Git.Run("ls-files", "-z");
        Assert.True(r.Stdout.Length > 200_000, "output of " + r.Stdout.Length + " bytes");
    }

    [Fact]
    public void FailuresCarryGitsMessageAndTheRepositoryIsNotModified()
    {
        GitFixture repo = GitFixture.Init(Tmp.Combine("repo"));
        repo.Write("a.txt", "a\n");
        repo.CommitAll("one");
        FilePath index = repo.Dir.Combine(".git/index");
        byte[] before = File.ReadAllBytes(index.Value);
        DateTime mtime = File.GetLastWriteTimeUtc(index.Value);
        GitRepository r = GitRepository.Locate(GitCli.ForDirectory(Tmp), repo.Dir)!;
        r.HeadSha();
        r.Git.Run("status", "--porcelain=v2", "-z");
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(index.Value));
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(index.Value)), "git must not rewrite the index");
        GitException e = Assert.Throws<GitException>(() => r.Git.Run("rev-parse", "--verify", "does-not-exist^{commit}"));
        Assert.True(e.ExitCode != 0);
        Assert.False(e.IsDubiousOwnership);
    }

    [Fact]
    public void IgnoredPathsListsWholeIgnoredFoldersWithoutTrackedFiles()
    {
        GitFixture repo = GitFixture.Init(Tmp.Combine("repo"));
        repo.Write(".gitignore", "build/\n*.log\n");
        repo.Write("src/A.java", "a\n");
        repo.CommitAll("base");
        repo.Write("build/classes/A.class", "x");
        repo.Write("build/other.txt", "x");
        repo.Write("traces.log", "x");
        repo.Write("src/fresh.txt", "x");
        GitRepository r = GitRepository.Locate(GitCli.ForDirectory(Tmp), repo.Dir)!;
        IReadOnlySet<FilePath> ignored = r.IgnoredPaths(repo.Dir);
        AssertSameSet([r.Root.Combine("build"), r.Root.Combine("traces.log")], ignored);
        // A project in a subdirectory only sees its own
        repo.Write("src/debug.log", "x");
        AssertSameSet([r.Root.Combine("src/debug.log")], r.IgnoredPaths(repo.Dir.Combine("src")));
    }

    [Fact]
    public void RecognisesTheDubiousOwnershipMessage()
    {
        GitException e = new("status", 128, "fatal: detected dubious ownership in repository at 'C:/x'");
        Assert.True(e.IsDubiousOwnership);
        Assert.Contains("exited with code 128", e.Message); // the hint is Presentation's (ReasonTextTest), the message is a diagnostic
    }
}
