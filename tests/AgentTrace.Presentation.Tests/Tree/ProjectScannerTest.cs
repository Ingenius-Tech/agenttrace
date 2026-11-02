using AgentTrace.Core;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Tree;

public sealed class ProjectScannerTest : TempDirTestBase
{
    private IgnoreRules Rules() => IgnoreRules.WithoutGit(Dir);

    private FileStatusIndex NoChanges() => FileStatusIndex.Empty(Dir);

    private void Write(string relative, int size)
    {
        string path = Dir.Combine(relative.Split('/')).Value;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    private static List<string> Names(ProjectListing listing) => [.. listing.Files.Select(f => f.Path.FileName).Order()];

    [Fact]
    public void ListsFilesOfAllLevelsWithTheirSize()
    {
        Write("a.txt", 10);
        Write("src/b.cs", 2000);
        Write("src/deep/c.cs", 5);
        ProjectListing listing = ProjectScanner.Scan(Dir, Rules(), NoChanges(), 100);
        Assert.False(listing.Truncated);
        Assert.Equal(["a.txt", "b.cs", "c.cs"], Names(listing));
        Assert.Equal(2000, listing.Files.Single(f => f.Path.FileName == "b.cs").Size);
        Assert.All(listing.Files, f => Assert.False(f.Ghost));
    }

    [Fact]
    public void UsesTheSameIgnoreRulesAsTheTree()
    {
        Write("src/a.cs", 1);
        Write("node_modules/x/y.js", 1);
        Write(".git/config", 1);
        Write("readme.md", 1);
        Assert.Equal(["a.cs", "readme.md"], Names(ProjectScanner.Scan(Dir, Rules(), NoChanges(), 100)));
        IgnoreRules git = IgnoreRules.WithGit(Dir, [Dir.Combine("src")]);
        Assert.Equal(["readme.md", "y.js"], Names(ProjectScanner.Scan(Dir, git, NoChanges(), 100)));
    }

    [Fact]
    public void DottedFilesAndFoldersAreNotFollowedAtAnyDepth()
    {
        Write("src/a.cs", 1);
        Write(".gitignore", 1);
        Write(".claude/worktrees/agent/big.bin", 5000);
        Write("src/.cache/x.bin", 1);
        Write("src/.hidden", 1);
        Assert.Equal(["a.cs"], Names(ProjectScanner.Scan(Dir, Rules(), NoChanges(), 100)));
    }

    [Fact]
    public void StopsAtTheLimitAndSaysSo()
    {
        for (int i = 0; i < 5; i++) Write("d" + i + "/f.txt", 1);
        ProjectListing listing = ProjectScanner.Scan(Dir, Rules(), NoChanges(), 3);
        Assert.True(listing.Truncated);
        Assert.Equal(3, listing.Files.Count);
        Assert.False(ProjectScanner.Scan(Dir, Rules(), NoChanges(), 5).Truncated);
    }

    [Fact]
    public void DeletedFilesThatAreStillInTheChangesComeAsGhostsWithSizeZero()
    {
        Write("live.txt", 3);
        FileStatusIndex index = FileStatusIndex.Of(Dir,
            [new ChangeEntry(new FileChange(Dir.Combine("old", "deleted.txt"), ChangeKind.Deleted, null), Attribution.Undated())], []);
        ProjectListing listing = ProjectScanner.Scan(Dir, Rules(), index, 100);
        ListedFile ghost = listing.Files.Single(f => f.Ghost);
        Assert.Equal(Dir.Combine("old", "deleted.txt"), ghost.Path);
        Assert.Equal(0, ghost.Size);
        Assert.Equal(2, listing.Files.Count);
    }

    [Fact]
    public void ALinkToAFolderIsNotFollowed()
    {
        Write("real/inside.txt", 1);
        try
        {
            Directory.CreateSymbolicLink(Dir.Combine("link").Value, Dir.Combine("real").Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // no privileges to create links: it cannot be checked here
        }
        Assert.Equal(["inside.txt"], Names(ProjectScanner.Scan(Dir, Rules(), NoChanges(), 100)));
    }

    [Fact]
    public void NeverThrowsOnAMissingRoot()
    {
        FilePath missing = Dir.Combine("missing");
        ProjectListing listing = ProjectScanner.Scan(missing, IgnoreRules.WithoutGit(missing), FileStatusIndex.Empty(missing), 10);
        Assert.Empty(listing.Files);
        Assert.False(listing.Truncated);
    }
}
