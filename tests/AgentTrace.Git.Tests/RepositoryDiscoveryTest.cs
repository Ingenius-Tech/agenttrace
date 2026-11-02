using AgentTrace.Core;
using AgentTrace.Testing;

namespace AgentTrace.Git.Tests;

public sealed class RepositoryDiscoveryTest : GitTestBase
{
    private DiscoveredRepositories Find() => RepositoryDiscovery.Find(GitCli.ForDirectory(Tmp), Tmp);

    private static void Mkdir(FilePath dir) => Directory.CreateDirectory(dir.Value);

    [Fact]
    public void AFolderWithoutRepositoriesFindsNone()
    {
        Mkdir(Tmp.Combine("a", "b"));
        Assert.Empty(Find().Repositories);
        Assert.Empty(Find().Problems);
    }

    [Fact]
    public void FindsRepositoriesAtSeveralLevelsSortedByPath()
    {
        GitFixture.Init(Tmp.Combine("zeta"));
        GitFixture.Init(Tmp.Combine("alpha"));
        GitFixture.Init(Tmp.Combine("group", "beta"));
        Mkdir(Tmp.Combine("loose"));
        Assert.Equal([Tmp.Combine("alpha"), Tmp.Combine("group", "beta"), Tmp.Combine("zeta")], Find().Repositories.Select(r => r.Root));
    }

    [Fact]
    public void DoesNotDescendIntoARepositoryNorPastTheDepthLimit()
    {
        GitFixture outer = GitFixture.Init(Tmp.Combine("outer"));
        GitFixture.Init(outer.Dir.Combine("inner"));
        GitFixture.Init(Tmp.Combine("n1", "n2", "n3", "deep"));
        Assert.Equal([outer.Dir], Find().Repositories.Select(r => r.Root));
    }

    [Fact]
    public void SkipsFoldersThatNeverHoldProjects()
    {
        GitFixture.Init(Tmp.Combine("node_modules", "package"));
        GitFixture.Init(Tmp.Combine("bin", "x"));
        GitFixture real = GitFixture.Init(Tmp.Combine("real"));
        Assert.Equal([real.Dir], Find().Repositories.Select(r => r.Root));
    }

    [Fact]
    public void FindsAWorktreeWhoseGitIsAFile()
    {
        GitFixture main = GitFixture.Init(Tmp.Combine("main"));
        main.Write("a.txt", "a");
        main.CommitAll("base");
        main.Run("worktree", "add", "-b", "branch", Tmp.Combine("work").Value);
        Assert.Equal([Tmp.Combine("main"), Tmp.Combine("work")], Find().Repositories.Select(r => r.Root));
    }

    [Fact]
    public void ALinkedFolderIsNotFollowed()
    {
        GitFixture target = GitFixture.Init(Tmp.Combine("outside", "repo"));
        FilePath inside = Tmp.Combine("project");
        Mkdir(inside);
        try
        {
            Directory.CreateSymbolicLink(inside.Combine("link").Value, target.Dir.Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("No privileges to create symbolic links");
        }
        Assert.Empty(RepositoryDiscovery.Find(GitCli.ForDirectory(Tmp), inside).Repositories);
    }

    [Fact]
    public void ACandidateThatIsNotARealRepositoryIsReportedAsAProblem()
    {
        Mkdir(Tmp.Combine("broken", ".git")); // empty .git folder: git does not recognize it
        GitFixture good = GitFixture.Init(Tmp.Combine("good"));
        DiscoveredRepositories found = Find();
        Assert.Equal([good.Dir], found.Repositories.Select(r => r.Root));
        Assert.DoesNotContain(found.Repositories, r => r.Root == Tmp.Combine("broken"));
    }
}
