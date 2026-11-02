using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests;

public sealed class FileStatusIndexTest
{
    private static readonly FilePath Root = new("project");
    private static readonly EventHeader H = new(new SessionId("s"), "t", null, null, new SourceRef(new FilePath("x"), 1));

    private static FilePath P(string relative) => Root.Combine(relative);

    private static ChangeEntry Entry(string file, ChangeKind kind, Confidence confidence)
    {
        Attribution attribution = confidence switch
        {
            Confidence.None => Attribution.None(),
            Confidence.Undated => Attribution.Undated(),
            _ => new Attribution(confidence, [new ToolCall(H, "c", "Bash", "{}")]),
        };
        return new ChangeEntry(new FileChange(P(file), kind, kind == ChangeKind.Deleted ? null : DateTimeOffset.UnixEpoch), attribution);
    }

    [Fact]
    public void AnEmptyIndexKnowsNothing()
    {
        FileStatusIndex index = FileStatusIndex.Empty(Root);
        Assert.Null(index.File(P("a.txt")));
        Assert.Equal(FolderSummary.Empty, index.Folder(Root));
        Assert.Empty(index.ChangedPaths());
        Assert.Empty(index.ChildNamesOnChangedPaths(Root));
    }

    [Fact]
    public void AttributionBecomesActor()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
        [
            Entry("exact.txt", ChangeKind.Created, Confidence.Exact),
            Entry("probable.txt", ChangeKind.Modified, Confidence.Probable),
            Entry("ambiguous.txt", ChangeKind.Modified, Confidence.Ambiguous),
            Entry("external.txt", ChangeKind.Modified, Confidence.None),
            Entry("deleted.txt", ChangeKind.Deleted, Confidence.Undated),
        ], []);
        Assert.Equal(Actor.Agent, index.File(P("exact.txt"))!.Actor);
        Assert.Equal(Actor.Agent, index.File(P("probable.txt"))!.Actor);
        Assert.Equal(Actor.Agent, index.File(P("ambiguous.txt"))!.Actor);
        Assert.Equal(Actor.External, index.File(P("external.txt"))!.Actor);
        Assert.Equal(Actor.Unknown, index.File(P("deleted.txt"))!.Actor);
        Assert.Equal(ChangeKind.Deleted, index.File(P("deleted.txt"))!.Kind);
    }

    [Fact]
    public void ReadsAreAddedToChangesAndAloneTheyHaveNoKind()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
            [Entry("a.txt", ChangeKind.Modified, Confidence.Exact)], new HashSet<FilePath> { P("a.txt"), P("read.txt") });
        FileStatus changedAndRead = index.File(P("a.txt"))!;
        Assert.Equal(ChangeKind.Modified, changedAndRead.Kind);
        Assert.True(changedAndRead.Read);
        FileStatus readOnly = index.File(P("read.txt"))!;
        Assert.Null(readOnly.Kind);
        Assert.True(readOnly.Read);
        Assert.True(!readOnly.Changed);
        Assert.Equal([P("a.txt")], index.ChangedPaths());
        Assert.Equal([P("a.txt"), P("read.txt")], index.ReadPaths());
    }

    [Fact]
    public void FoldersAggregateEverythingBelowThem()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
        [
            Entry("src/main/A.java", ChangeKind.Modified, Confidence.Exact),
            Entry("src/main/B.java", ChangeKind.Created, Confidence.None),
            Entry("src/test/C.java", ChangeKind.Deleted, Confidence.Undated),
            Entry("README.md", ChangeKind.Modified, Confidence.Probable),
        ], []);
        Assert.Equal(new FolderSummary(1, 1, 0, 1), index.Folder(P("src/main")));
        Assert.Equal(new FolderSummary(0, 0, 1, 0), index.Folder(P("src/test")));
        Assert.Equal(new FolderSummary(1, 1, 1, 1), index.Folder(P("src")));
        Assert.Equal(new FolderSummary(1, 2, 1, 1), index.Folder(Root));
        Assert.Equal(FolderSummary.Empty, index.Folder(P("docs")));
        Assert.Equal(4, index.Folder(Root).Total);
    }

    [Fact]
    public void StrongestChangeInAFolderIsDeletedThenModifiedThenCreated()
    {
        Assert.Equal(ChangeKind.Deleted, new FolderSummary(5, 5, 1, 0).Strongest());
        Assert.Equal(ChangeKind.Modified, new FolderSummary(5, 1, 0, 0).Strongest());
        Assert.Equal(ChangeKind.Created, new FolderSummary(1, 0, 0, 0).Strongest());
        Assert.Null(FolderSummary.Empty.Strongest());
    }

    [Fact]
    public void ChildNamesLeadToChangesIncludingIntermediateFolders()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
        [
            Entry("src/old/Deleted.java", ChangeKind.Deleted, Confidence.Undated),
            Entry("src/Other.java", ChangeKind.Modified, Confidence.Exact),
        ], []);
        Assert.Equal(new HashSet<string> { "src" }, index.ChildNamesOnChangedPaths(Root));
        Assert.Equal(new HashSet<string> { "old", "Other.java" }, index.ChildNamesOnChangedPaths(P("src")));
        Assert.Equal(new HashSet<string> { "Deleted.java" }, index.ChildNamesOnChangedPaths(P("src/old")));
        Assert.Empty(index.ChildNamesOnChangedPaths(P("src/old/Deleted.java")));
    }

    [Fact]
    public void PathsOutsideTheProjectAreIgnored()
    {
        FilePath outside = Root.Parent!.Value.Combine("other").Combine("f.txt");
        FileStatusIndex index = FileStatusIndex.Of(Root,
            [new ChangeEntry(new FileChange(outside, ChangeKind.Created, DateTimeOffset.UnixEpoch), Attribution.None())],
            new HashSet<FilePath> { outside });
        Assert.Null(index.File(outside));
        Assert.Empty(index.ChangedPaths());
        Assert.Empty(index.ReadPaths());
        Assert.Equal(FolderSummary.Empty, index.Folder(Root));
    }

    [Fact]
    public void ChangedPathsAreSortedAndPathsAreNormalized()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
        [
            Entry("z.txt", ChangeKind.Created, Confidence.None),
            Entry("a/../b.txt", ChangeKind.Created, Confidence.None),
            Entry("a.txt", ChangeKind.Created, Confidence.None),
        ], []);
        Assert.Equal([P("a.txt"), P("b.txt"), P("z.txt")], index.ChangedPaths());
        Assert.NotNull(index.File(P("a/../b.txt"))); // the query is normalized too
    }

    [Fact]
    public void LookupsIgnoreCaseWhereThePlatformDoes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        FileStatusIndex index = FileStatusIndex.Of(Root, [Entry("Folder/File.TXT", ChangeKind.Created, Confidence.None)], []);
        Assert.NotNull(index.File(new FilePath(P("Folder/File.TXT").Value.ToUpperInvariant())));
        Assert.Equal(1, index.Folder(new FilePath(P("Folder").Value.ToLowerInvariant())).Total);
    }

    // New compared to Java

    [Fact]
    public void TheRootItselfIsNotAFileOfTheIndex()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
            [new ChangeEntry(new FileChange(Root, ChangeKind.Modified, DateTimeOffset.UnixEpoch), Attribution.None())],
            new HashSet<FilePath> { Root });

        Assert.Null(index.File(Root));
        Assert.Empty(index.ChangedPaths());
        Assert.Equal(Root, index.Root);
    }

    [Fact]
    public void ASiblingFolderWithTheSamePrefixIsOutsideTheProject()
    {
        FilePath sibling = Root.Parent!.Value.Combine("project-other", "f.txt");

        FileStatusIndex index = FileStatusIndex.Of(Root,
            [new ChangeEntry(new FileChange(sibling, ChangeKind.Created, DateTimeOffset.UnixEpoch), Attribution.None())], []);

        Assert.Empty(index.ChangedPaths());
    }

    [Fact]
    public void ALaterEntryForTheSamePathReplacesTheEarlierOne()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
        [
            Entry("a.txt", ChangeKind.Created, Confidence.None),
            Entry("a.txt", ChangeKind.Modified, Confidence.Exact),
        ], []);

        Assert.Equal(ChangeKind.Modified, index.File(P("a.txt"))!.Kind);
        Assert.Equal(Actor.Agent, index.File(P("a.txt"))!.Actor);
    }
}
