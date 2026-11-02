using AgentTrace.Core.Knowledge;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Core.Tests.Knowledge;

public sealed class KnowledgeBuilderTest
{
    private static readonly FilePath A = new("a.txt");
    private static readonly FilePath B = new("b.txt");
    private static readonly FilePath C = new("c.txt");

    private static EventHeader H(string? sub) => new(new SessionId("s"), "test", sub, null, new SourceRef(new FilePath("t"), 1));

    private static AgentEvent User() => new UserMessage(H(null), "p");

    private static AgentEvent Read(FilePath p) => new FileRead(H(null), p, "r");

    private static AgentEvent Change(FilePath p, ChangeKind kind) => new FileChanged(H(null), p, kind, "w");

    private static KnowledgeGraph Build(params AgentEvent[] events)
    {
        var b = new KnowledgeBuilder();
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }

    private static Edge? EdgeOf(KnowledgeGraph g, FilePath from, FilePath to, Relation r) =>
        g.Edges.FirstOrDefault(e => e.From == from && e.To == to && e.Relation == r);

    private static AgentEvent Call(string id, FilePath? target) => new ToolCall(H(null), id, "T", AgentEvent.ToolKind.Read, "", "{}") { Target = target };

    private static AgentEvent Fail(string id) => new ToolResult(H(null), id, true, "boom");

    [Fact]
    public void AnErrorIsAttributedToTheTargetOfItsCall()
    {
        KnowledgeGraph g = Build(User(), Call("c1", A), Fail("c1"), Call("c2", A), Fail("c2"), Call("c3", A), new ToolResult(H(null), "c3", false, "ok"));
        Node a = g.FindNode(A)!;
        Assert.Equal(2, a.Errors);
        Assert.Equal(0, a.Weight); // errors carry no weight: Knowledge does not change
        Assert.Equal(1, a.TurnsTouched);
        Assert.Equal(0, g.UnplacedErrors);
        Assert.True(g.TargetsKnown);
    }

    [Fact]
    public void AnErrorWithoutTargetIsUnplacedAndAnUnknownCallIsIgnored()
    {
        KnowledgeGraph g = Build(User(), Call("c1", null), Fail("c1"), Fail("nope"));
        Assert.Equal(1, g.UnplacedErrors);
        Assert.Empty(g.Nodes);
        Assert.False(g.TargetsKnown);
    }

    [Fact]
    public void DottedFilesAndFoldersOfTheProjectAreNotFollowed()
    {
        FilePath root = new(OperatingSystem.IsWindows() ? @"C:\project" : "/project");
        var b = new KnowledgeBuilder(root);
        b.Accept(User());
        b.Accept(Read(root.Combine("src", "a.cs")));
        b.Accept(Read(root.Combine(".claude", "memory.md")));
        b.Accept(Change(root.Combine(".gitignore"), ChangeKind.Modified));
        b.Accept(Change(root.Combine("src", ".cache", "x.bin"), ChangeKind.Created));
        b.Accept(Change(root.Combine("src", "b.cs"), ChangeKind.Modified));
        KnowledgeGraph g = b.Snapshot();
        Assert.Equal([root.Combine("src", "a.cs"), root.Combine("src", "b.cs")], g.Nodes.Select(n => n.Path).OrderBy(p => p.Value).ToList());
        Assert.Single(g.Edges);
    }

    [Fact]
    public void ErrorsOnFilesOutsideTheProjectAreUnplaced()
    {
        FilePath root = new FilePath("proj");
        var b = new KnowledgeBuilder(root);
        foreach (AgentEvent e in new[] { User(), Call("c1", new FilePath("other").Combine("x")), Fail("c1"), Call("c2", root.Combine("x")), Fail("c2") }) b.Accept(e);
        KnowledgeGraph g = b.Snapshot();
        Assert.Equal(1, g.UnplacedErrors);
        Assert.Equal(1, g.FindNode(root.Combine("x"))!.Errors);
        Assert.Single(g.Nodes);
        Assert.True(g.TargetsKnown);
    }

    [Fact]
    public void EmptyGraph()
    {
        KnowledgeGraph g = new KnowledgeBuilder().Snapshot();
        Assert.True(g.Nodes.Count == 0 && g.Edges.Count == 0);
        Assert.Equal(0, g.Turns);
    }

    [Fact]
    public void CountsReadsAndChangesPerFileAndKeepsTheLastChange()
    {
        KnowledgeGraph g = Build(User(), Read(A), Read(A), Change(A, ChangeKind.Modified), Change(B, ChangeKind.Created),
            Change(B, ChangeKind.Modified), Change(B, ChangeKind.Deleted));
        Node a = g.FindNode(A)!;
        Assert.Equal(2, a.Reads);
        Assert.Equal(1, a.Modified);
        Assert.Equal(ChangeKind.Modified, a.LastChange);
        Node b = g.FindNode(B)!;
        Assert.Equal(1, b.Created);
        Assert.Equal(1, b.Modified);
        Assert.Equal(1, b.Deleted);
        Assert.Equal(ChangeKind.Deleted, b.LastChange);
        Assert.Equal(3, b.AgentChanges);
        Assert.Equal(3, b.Weight);
        Assert.True(b.Changed && !b.OnlyExternal);
    }

    [Fact]
    public void AFileOnlyReadHasNoLastChange()
    {
        Node a = Build(User(), Read(A)).FindNode(A)!;
        Assert.Null(a.LastChange);
        Assert.False(a.Changed);
        Assert.Equal(1, a.Weight);
    }

    [Fact]
    public void ReadBeforeEditPointsFromTheReadToTheEditedFile()
    {
        KnowledgeGraph g = Build(User(), Read(A), Read(B), Change(C, ChangeKind.Modified));
        Assert.Equal(1, EdgeOf(g, A, C, Relation.ReadBeforeEdit)!.Weight);
        Assert.Equal(1, EdgeOf(g, B, C, Relation.ReadBeforeEdit)!.Weight);
        Assert.Equal(2, g.Edges.Count);
    }

    [Fact]
    public void AReadAfterTheEditIsNotAReasonForTheEdit()
    {
        KnowledgeGraph g = Build(User(), Change(C, ChangeKind.Modified), Read(A));
        Assert.Empty(g.Edges);
    }

    [Fact]
    public void ReadingAndEditingTheSameFileCreatesNoSelfEdge()
    {
        Assert.Empty(Build(User(), Read(A), Change(A, ChangeKind.Modified)).Edges);
    }

    [Fact]
    public void FilesChangedInTheSameTurnAreCoChangedOnceWhateverTheOrder()
    {
        KnowledgeGraph g = Build(User(), Change(B, ChangeKind.Modified), Change(A, ChangeKind.Modified), Change(A, ChangeKind.Modified));
        Assert.Single(g.Edges);
        Edge e = EdgeOf(g, A, B, Relation.CoChanged)!;
        Assert.Equal(1, e.Weight); // a pair adds one point per turn even if edited several times
    }

    [Fact]
    public void TheWeightCountsTurnsNotEvents()
    {
        KnowledgeGraph g = Build(User(), Change(A, ChangeKind.Modified), Change(B, ChangeKind.Modified),
            User(), Change(A, ChangeKind.Modified), Change(B, ChangeKind.Modified));
        Assert.Equal(2, EdgeOf(g, A, B, Relation.CoChanged)!.Weight);
        Assert.Equal(2, g.FindNode(A)!.TurnsTouched);
        Assert.Equal(2, g.Turns);
    }

    [Fact]
    public void FirstAndLastTurnMarkWhenAFileWasTouched()
    {
        KnowledgeGraph g = Build(User(), Read(A), User(), Change(B, ChangeKind.Modified), User(), Read(A), Change(B, ChangeKind.Modified));
        Assert.Equal((0, 2), (g.FindNode(A)!.FirstTurn, g.FindNode(A)!.LastTurn));
        Assert.Equal((1, 2), (g.FindNode(B)!.FirstTurn, g.FindNode(B)!.LastTurn));
    }

    [Fact]
    public void AContinuationWithoutRequestOpensTurnZero()
    {
        KnowledgeGraph g = Build(Read(A));
        Assert.Equal((0, 0), (g.FindNode(A)!.FirstTurn, g.FindNode(A)!.LastTurn));
    }

    [Fact]
    public void RelationsDoNotCrossTurns()
    {
        KnowledgeGraph g = Build(User(), Read(A), User(), Change(B, ChangeKind.Modified));
        Assert.Empty(g.Edges);
    }

    [Fact]
    public void WorkAfterTurnEndedOpensAContinuationTurn()
    {
        var b = new KnowledgeBuilder();
        b.Accept(User());
        b.Accept(Read(A));
        b.Accept(new TurnEnded(H(null), null));
        b.Accept(Change(B, ChangeKind.Modified));
        KnowledgeGraph g = b.Snapshot();
        Assert.Empty(g.Edges); // the turn ended between the read and the change
        Assert.Equal(2, g.Turns);
    }

    [Fact]
    public void ExternalChangesCountOnTheNodeButCreateNoRelations()
    {
        KnowledgeGraph g = Build(User(), Read(A), new FileChanged(H(null), B, ChangeKind.Modified, null), Change(C, ChangeKind.Modified));
        Node b = g.FindNode(B)!;
        Assert.Equal(1, b.External);
        Assert.True(b.OnlyExternal && b.Changed);
        Assert.Equal(ChangeKind.Modified, b.LastChange);
        Assert.True(g.Edges.All(e => e.From != B && e.To != B));
        Assert.Single(g.Edges); // only A→C
    }

    [Fact]
    public void SubagentsShareTheTurnOfTheMainThread()
    {
        var b = new KnowledgeBuilder();
        b.Accept(User());
        b.Accept(new FileRead(H("sub"), A, "r"));
        b.Accept(new UserMessage(H("sub"), "task"));
        b.Accept(new FileChanged(H(null), B, ChangeKind.Modified, "w"));
        b.Accept(new TurnEnded(H("sub"), null));
        b.Accept(new FileChanged(H(null), C, ChangeKind.Modified, "w"));
        KnowledgeGraph g = b.Snapshot();
        Assert.Equal(1, g.Turns);
        Assert.Equal(1, EdgeOf(g, A, B, Relation.ReadBeforeEdit)!.Weight);
        Assert.Equal(1, EdgeOf(g, A, C, Relation.ReadBeforeEdit)!.Weight);
        Assert.Equal(1, EdgeOf(g, B, C, Relation.CoChanged)!.Weight);
    }

    [Fact]
    public void EdgesOfListsTheRelationsOfAFileHeaviestFirst()
    {
        KnowledgeGraph g = Build(User(), Change(A, ChangeKind.Modified), Change(B, ChangeKind.Modified),
            User(), Change(A, ChangeKind.Modified), Change(B, ChangeKind.Modified), Change(C, ChangeKind.Modified));
        IReadOnlyList<Edge> edges = g.EdgesOf(A);
        Assert.Equal(2, edges.Count);
        Assert.Equal(B, edges[0].To);
        Assert.Equal(2, edges[0].Weight);
    }

    [Fact]
    public void FilesOutsideTheProjectAreNotPartOfItsKnowledge()
    {
        var root = new FilePath("project");
        var b = new KnowledgeBuilder(root);
        b.Accept(User());
        b.Accept(new FileRead(H(null), root.Combine("src").Combine("A.java"), "r"));
        b.Accept(new FileRead(H(null), new FilePath("other").Combine("memory.md"), "r"));
        b.Accept(new FileChanged(H(null), new FilePath("other").Combine("tmp.txt"), ChangeKind.Created, "w"));
        b.Accept(new FileChanged(H(null), root.Combine("B.java"), ChangeKind.Modified, "w"));
        KnowledgeGraph g = b.Snapshot();
        Assert.Equal(2, g.Nodes.Count);
        Assert.Single(g.Edges);
    }

    [Fact]
    public void SnapshotsAreIndependentOfLaterEvents()
    {
        var b = new KnowledgeBuilder();
        b.Accept(User());
        b.Accept(Read(A));
        KnowledgeGraph first = b.Snapshot();
        b.Accept(Change(B, ChangeKind.Modified));
        Assert.Single(first.Nodes);
        Assert.Equal(2, b.Snapshot().Nodes.Count);
    }

    [Fact]
    public void IgnoresEventsThatSayNothingAboutFiles()
    {
        KnowledgeGraph g = Build(User(), new ToolCall(H(null), "c", "Bash", "{}"), new AssistantText(H(null), "x"),
            new Skipped(H(null), "x", SkipReason.Ignored));
        Assert.Empty(g.Nodes);
    }

    // New compared to Java

    [Fact]
    public void AcceptsEveryEventVariantWithoutFailing()
    {
        var b = new KnowledgeBuilder();

        foreach (AgentEvent e in EventSamples.OfEveryVariant()) b.Accept(e);

        KnowledgeGraph g = b.Snapshot();
        Assert.Single(g.Nodes);
        Assert.Equal(new FilePath("a.txt"), g.Nodes[0].Path);
    }

    [Fact]
    public void NodesAndEdgesKeepInsertionOrder()
    {
        KnowledgeGraph g = Build(User(), Read(C), Read(A), Change(B, ChangeKind.Modified));

        Assert.Equal([C, A, B], g.Nodes.Select(n => n.Path).ToArray());
        Assert.Equal([(C, B), (A, B)], g.Edges.Select(e => (e.From, e.To)).ToArray());
    }

    [Fact]
    public void CoChangedEdgesPointFromTheSmallerPathTextToTheLarger()
    {
        KnowledgeGraph g = Build(User(), Change(C, ChangeKind.Modified), Change(A, ChangeKind.Modified), Change(B, ChangeKind.Modified));

        Assert.NotNull(EdgeOf(g, A, C, Relation.CoChanged));
        Assert.NotNull(EdgeOf(g, B, C, Relation.CoChanged));
        Assert.NotNull(EdgeOf(g, A, B, Relation.CoChanged));
        Assert.Equal(3, g.Edges.Count);
    }

    [Fact]
    public void GraphsWithTheSameContentAreEqual()
    {
        Assert.Equal(Build(User(), Read(A), Change(B, ChangeKind.Modified)), Build(User(), Read(A), Change(B, ChangeKind.Modified)));
    }
}
