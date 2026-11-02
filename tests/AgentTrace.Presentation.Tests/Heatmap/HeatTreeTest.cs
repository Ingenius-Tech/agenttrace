using AgentTrace.Core;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Tests.Heatmap.HeatFixture;

namespace AgentTrace.Presentation.Tests.Heatmap;

public sealed class HeatTreeTest
{
    [Fact]
    public void FoldersAddUpWhatTheyContain()
    {
        HeatTree tree = Build(
            Listing(Listed("src/a.cs", 2000), Listed("src/sub/b.cs", 500), Listed("README.md", 100)),
            Session(3, Touched("src/a.cs", reads: 2, modified: 1, errors: 1, turns: 2, lastTurn: 2, last: ChangeKind.Modified),
                Touched("src/sub/b.cs", reads: 3, lastTurn: 1)));
        HeatNode src = tree.Find(P("src"))!;
        Assert.True(src.Directory);
        Assert.Equal(2500, src.Size);
        Assert.Equal(2000 + 1024, src.AreaBytes); // b.cs weighs 1 KiB at least
        Assert.Equal(new FileActivity(5, 0, 1, 0, 0, 1), src.Activity);
        Assert.Equal(2, src.Files);
        Assert.Equal(2, src.TouchedFiles);
        Assert.Equal(2, src.LastTurn);
        Assert.Equal(3, tree.Root.Files);
        Assert.Equal(2, tree.Root.TouchedFiles);
        Assert.Equal(2600, tree.Root.Size);
        Assert.Equal(2, tree.LastTurn);
        Assert.Equal(-1, tree.Find(P("README.md"))!.LastTurn);
    }

    [Fact]
    public void TouchedFilesEnterEvenIfTheListingDoesNotHaveThem()
    {
        HeatTree tree = Build(Listing(Listed("a.txt", 10)), Session(1, Touched("ignorado/gen/out.js", modified: 1, last: ChangeKind.Modified)));
        HeatNode file = tree.Find(P("ignorado/gen/out.js"))!;
        Assert.False(file.Directory);
        Assert.Equal(0, file.Size);
        Assert.Equal(1024, file.AreaBytes);
        Assert.True(tree.Find(P("ignorado/gen"))!.Directory);
        Assert.Equal(2, tree.Root.Files);
    }

    [Fact]
    public void WhatIsOutsideTheProjectIsLeftOut()
    {
        FilePath outside = new(Path.Combine(Path.GetTempPath(), "other-project", "x.txt"));
        var stray = new AgentTrace.Core.Knowledge.KnowledgeGraph.Node(outside, null, 1, 0, 0, 0, 0, 1, 0, 0);
        HeatTree tree = Build(Listing(Listed("a.txt", 10)), Session(1, stray, Touched("a.txt", reads: 1)));
        Assert.Equal(1, tree.Root.Files);
        Assert.Null(tree.Find(outside));
        Assert.Equal(1, tree.Root.Activity.Reads);
    }

    [Fact]
    public void TheStateComesFromGitFirstAndThenFromTheGraph()
    {
        FileStatusIndex index = FileStatusIndex.Of(Root,
            [new ChangeEntry(new FileChange(P("a.txt"), ChangeKind.Created, null), Attribution.Undated())], []);
        HeatTree tree = Build(Listing(Listed("a.txt", 10), Listed("b.txt", 10), Listed("c.txt", 10)),
            Session(1, Touched("a.txt", modified: 1, last: ChangeKind.Modified), Touched("b.txt", modified: 1, last: ChangeKind.Modified)), index);
        Assert.Equal(ChangeKind.Created, tree.Find(P("a.txt"))!.Kind);
        Assert.Equal(ChangeKind.Modified, tree.Find(P("b.txt"))!.Kind);
        Assert.Null(tree.Find(P("c.txt"))!.Kind);
    }

    [Fact]
    public void DeletedFilesAreGhosts()
    {
        var ghost = new ListedFile(P("viejo.txt"), 0, true);
        HeatTree tree = Build(Listing(ghost, Listed("b.txt", 10)), Session(1, Touched("b.txt", deleted: 1, last: ChangeKind.Deleted)));
        Assert.True(tree.Find(P("viejo.txt"))!.Ghost);
        Assert.True(tree.Find(P("b.txt"))!.Ghost);
    }

    [Fact]
    public void ErrorsAndTruncationAreCarriedOver()
    {
        var graph = new AgentTrace.Core.Knowledge.KnowledgeGraph(ValueList<AgentTrace.Core.Knowledge.KnowledgeGraph.Node>.Empty,
            ValueList<AgentTrace.Core.Knowledge.KnowledgeGraph.Edge>.Empty, 0, UnplacedErrors: 4, TargetsKnown: true);
        HeatTree tree = HeatTree.Build(Root, new ProjectListing(ValueList<ListedFile>.Empty, true), graph, FileStatusIndex.Empty(Root));
        Assert.Equal(4, tree.UnplacedErrors);
        Assert.True(tree.TargetsKnown);
        Assert.True(tree.Truncated);
        Assert.Equal(-1, tree.LastTurn);
        Assert.Empty(tree.Root.Children);
    }

    [Fact]
    public void TextSummarizesAFolderAndAFile()
    {
        HeatTree tree = Build(Listing(Listed("src/a.cs", 2048), Listed("src/b.cs", 10)),
            Session(2, Touched("src/a.cs", reads: 1, created: 1, modified: 2, errors: 1, turns: 2, last: ChangeKind.Created)));
        Assert.Equal("src/a.cs\n2.0 KB\n1 read · 3 writes (1 created, 2 modified) · 1 error\n2 turns",
            HeatText.Tooltip(tree.Find(P("src/a.cs"))!, tree));
        Assert.Equal("src\n2 files, 1 touched · 2.0 KB\n1 read · 3 writes (1 created, 2 modified) · 1 error",
            HeatText.Tooltip(tree.Find(P("src"))!, tree));
        Assert.Equal("src/b.cs\n10 B\nNo activity", HeatText.Tooltip(tree.Find(P("src/b.cs"))!, tree));
        Assert.Equal("2 files, 1 touched · 1 read · 3 writes · 1 error", HeatText.Summary(tree, ActivityFilter.All));
        Assert.Equal("2 files, 1 touched · 1 error", HeatText.Summary(tree, new ActivityFilter(false, false, true)));
    }

    [Fact]
    public void SpanishTextsAreUnchanged()
    {
        using var _ = CultureScope.Spanish();
        HeatTree tree = Build(Listing(Listed("src/a.cs", 2048), Listed("src/b.cs", 10)),
            Session(2, Touched("src/a.cs", reads: 1, created: 1, modified: 2, errors: 1, turns: 2, last: ChangeKind.Created)));
        Assert.Equal("src/a.cs\n2,0 KB\n1 lectura · 3 escrituras (1 creada, 2 modificadas) · 1 error\n2 turnos", // lang-ok
            HeatText.Tooltip(tree.Find(P("src/a.cs"))!, tree));
        Assert.Equal("src/b.cs\n10 B\nSin actividad", HeatText.Tooltip(tree.Find(P("src/b.cs"))!, tree)); // lang-ok
        Assert.Equal("2 archivos, 1 tocado · 1 lectura · 3 escrituras · 1 error", HeatText.Summary(tree, ActivityFilter.All)); // lang-ok
    }

    [Fact]
    public void SummaryMentionsUnplacedErrorsAndTruncation()
    {
        var graph = new AgentTrace.Core.Knowledge.KnowledgeGraph(ValueList<AgentTrace.Core.Knowledge.KnowledgeGraph.Node>.Empty,
            ValueList<AgentTrace.Core.Knowledge.KnowledgeGraph.Edge>.Empty, 1, UnplacedErrors: 2);
        HeatTree tree = HeatTree.Build(Root, new ProjectListing(ValueList<ListedFile>.Empty, true), graph, FileStatusIndex.Empty(Root));
        Assert.Contains("2 errors without a file", HeatText.Summary(tree, ActivityFilter.All));
        Assert.Contains("Showing part of the project", HeatText.Summary(tree, ActivityFilter.All));
    }
}
