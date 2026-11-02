using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Tree;

public sealed class DirectoryListerTest : TempDirTestBase
{
    private IgnoreRules Rules() => IgnoreRules.WithoutGit(Dir);

    private FileStatusIndex NoChanges() => FileStatusIndex.Empty(Dir);

    private static List<string> Names(IReadOnlyList<FileNode> nodes) => [.. nodes.Select(n => n.Name)];

    private void Folder(string name) => Directory.CreateDirectory(Dir.Combine(name).Value);

    private void Text(string name) => File.WriteAllText(Dir.Combine(name).Value, "x");

    [Fact]
    public void FoldersComeFirstThenNamesIgnoringCase()
    {
        Folder("zeta");
        Folder("Alfa");
        Text("beta.txt");
        Text("Alpha.txt");
        Text("a.txt");
        Assert.Equal(["Alfa", "zeta", "a.txt", "Alpha.txt", "beta.txt"], Names(DirectoryLister.Children(Dir, Rules(), NoChanges())));
    }

    [Fact]
    public void DotFilesAreShownButIgnoredFoldersAreNot()
    {
        Text(".gitignore");
        Folder(".git");
        Folder("node_modules");
        Folder("src");
        Assert.Equal(["src", ".gitignore"], Names(DirectoryLister.Children(Dir, Rules(), NoChanges())));
    }

    [Fact]
    public void GitIgnoredEntriesAreHidden()
    {
        Folder("build");
        Text("x.log");
        Text("real.txt");
        IgnoreRules withGit = IgnoreRules.WithGit(Dir, [Dir.Combine("build"), Dir.Combine("x.log")]);
        Assert.Equal(["real.txt"], Names(DirectoryLister.Children(Dir, withGit, NoChanges())));
    }

    [Fact]
    public void DeletedFilesStayAsGhostsWhileTheyAreInTheChangeList()
    {
        Text("live.txt");
        FileStatusIndex index = FileStatusIndex.Of(Dir,
            [new ChangeEntry(new FileChange(Dir.Combine("deleted.txt"), ChangeKind.Deleted, null), Attribution.Undated())], []);
        IReadOnlyList<FileNode> nodes = DirectoryLister.Children(Dir, Rules(), index);
        Assert.Equal(["deleted.txt", "live.txt"], Names(nodes));
        FileNode ghost = nodes[0];
        Assert.True(ghost.Ghost);
        Assert.False(ghost.Directory);
        Assert.False(nodes[1].Ghost);
    }

    [Fact]
    public void ADeletedFolderIsAGhostFolderWhoseChildrenAreGhosts()
    {
        FileStatusIndex index = FileStatusIndex.Of(Dir,
        [
            new ChangeEntry(new FileChange(Dir.Combine("old", "A.java"), ChangeKind.Deleted, null), Attribution.Undated()),
            new ChangeEntry(new FileChange(Dir.Combine("old", "sub", "B.java"), ChangeKind.Deleted, null), Attribution.Undated()),
        ], []);
        IReadOnlyList<FileNode> top = DirectoryLister.Children(Dir, Rules(), index);
        Assert.Equal(["old"], Names(top));
        Assert.True(top[0].Ghost);
        Assert.True(top[0].Directory);
        IReadOnlyList<FileNode> inside = DirectoryLister.Children(Dir.Combine("old"), Rules(), index);
        Assert.Equal(["sub", "A.java"], Names(inside));
        Assert.True(inside[0].Directory);
        Assert.False(inside[1].Directory);
    }

    [Fact]
    public void AChangedFileThatExistsIsNotAGhost()
    {
        Text("a.txt");
        FileStatusIndex index = FileStatusIndex.Of(Dir,
            [new ChangeEntry(new FileChange(Dir.Combine("a.txt"), ChangeKind.Modified, DateTimeOffset.UnixEpoch), Attribution.None())], []);
        IReadOnlyList<FileNode> nodes = DirectoryLister.Children(Dir, Rules(), index);
        Assert.Single(nodes);
        Assert.False(nodes[0].Ghost);
    }

    [Fact]
    public void ALinkToAFolderIsShownAsAFolderButIsNotExpandable()
    {
        FilePath target = Dir.Combine("real");
        Directory.CreateDirectory(target.Value);
        FilePath link = Dir.Combine("link");
        try
        {
            Directory.CreateSymbolicLink(link.Value, target.Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("Cannot create symbolic links here: " + e.Message);
        }
        IReadOnlyList<FileNode> nodes = DirectoryLister.Children(Dir, Rules(), NoChanges());
        FileNode node = nodes.Single(n => n.Name == "link");
        Assert.True(node.Directory);
        Assert.True(node.Symlink);
        Assert.False(node.Expandable);
        Assert.True(nodes.Single(n => n.Name == "real").Expandable);
    }

    [Fact]
    public void AWindowsJunctionIsRecognisedAsALinkAndIsNotExpandable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "junctions only exist on Windows");
        FilePath target = Dir.Combine("target");
        Directory.CreateDirectory(target.Value);
        FilePath junction = Dir.Combine("junction");
        var info = new ProcessStartInfo("cmd", ["/c", "mklink", "/J", junction.Value, target.Value])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process p = Process.Start(info)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.SkipUnless(p.ExitCode == 0, "could not create the junction with mklink /J");
        FileNode node = DirectoryLister.Children(Dir, Rules(), NoChanges()).Single(n => n.Name == "junction");
        Assert.True(node.Symlink, "a Windows junction must be treated as a link (it is not followed)");
        Assert.True(node.Directory);
        Assert.False(node.Expandable);
    }

    [Fact]
    public void AMissingOrUnreadableFolderIsEmptyAndNeverThrows() =>
        Assert.Empty(DirectoryLister.Children(Dir.Combine("missing"), Rules(), NoChanges()));

    [Fact]
    public void ListingFiveThousandEntriesIsFast()
    {
        for (int i = 0; i < 5000; i++) Text("file-" + i + ".txt");
        long start = Stopwatch.GetTimestamp();
        IReadOnlyList<FileNode> nodes = DirectoryLister.Children(Dir, Rules(), NoChanges());
        TimeSpan took = Stopwatch.GetElapsedTime(start);
        Assert.Equal(5000, nodes.Count);
        Assert.True(took.TotalMilliseconds < 1000, "took " + took.TotalMilliseconds + " ms (target < 200 ms on a normal machine)");
    }
}
