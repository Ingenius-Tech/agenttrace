using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Knowledge;

public sealed class NodeDetailTest
{
    private static readonly FilePath Root = new("proy");
    private static readonly FilePath A = Root.Combine("src").Combine("A.java");
    private static readonly FilePath B = Root.Combine("B.java");

    private static EventHeader H() => new(new SessionId("s"), "test", null, null, new SourceRef(new FilePath("t"), 1));

    private static KnowledgeGraph BuildGraph()
    {
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(H(), "p"));
        b.Accept(new FileRead(H(), A, "r"));
        b.Accept(new FileChanged(H(), B, ChangeKind.Modified, "w"));
        b.Accept(new FileChanged(H(), Root.Combine("ext.txt"), ChangeKind.Created, null));
        return b.Snapshot();
    }

    private static string? Field(NodeDetail d, string label) => d.Fields.Where(f => f.Label == label).Select(f => f.Value).FirstOrDefault();

    [Fact]
    public void DescribesTheFileWithItsPathRelativeToTheProject()
    {
        NodeDetail d = NodeDetail.Of(BuildGraph(), B, Root)!;
        Assert.Equal("B.java", d.Title);
        Assert.Equal("modified", Field(d, "State"));
        Assert.Equal("1", Field(d, "Modifications"));
        Assert.Equal("1 of 1", Field(d, "Turns"));
        Assert.True(d.Changed);
        Assert.Equal(Path.Combine("src", "A.java"), NodeDetail.Of(BuildGraph(), A, Root)!.Title);
        Assert.Equal(A.ToString(), NodeDetail.Of(BuildGraph(), A, null)!.Title);
    }

    [Fact]
    public void RelationsAreWordedFromTheSelectedFile()
    {
        NodeDetail b = NodeDetail.Of(BuildGraph(), B, Root)!;
        Assert.Single(b.Related);
        Assert.Equal(A, b.Related[0].Path);
        Assert.Equal("that one was read before this one was edited", b.Related[0].Relation);
        NodeDetail a = NodeDetail.Of(BuildGraph(), A, Root)!;
        Assert.Equal("this one was read before that one was edited", a.Related[0].Relation);
        Assert.Equal("read only", Field(a, "State"));
        Assert.False(a.Changed);
    }

    [Fact]
    public void ExternalChangesAreSaidPlainly()
    {
        NodeDetail e = NodeDetail.Of(BuildGraph(), Root.Combine("ext.txt"), Root)!;
        Assert.Equal("created (external)", Field(e, "State"));
        Assert.Contains("not made by the agent", Field(e, "External changes"));
        Assert.Empty(e.Related);
    }

    [Fact]
    public void RelatedFilesAreCappedAndAnUnknownFileGivesNothing()
    {
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(H(), "p"));
        for (int i = 0; i < 30; i++) b.Accept(new FileChanged(H(), Root.Combine("f" + i), ChangeKind.Modified, "w"));
        Assert.Equal(NodeDetail.MaxRelated, NodeDetail.Of(b.Snapshot(), Root.Combine("f0"), Root)!.Related.Count);
        Assert.Null(NodeDetail.Of(b.Snapshot(), Root.Combine("nope"), Root));
    }

    [Fact]
    public void ErrorsAreShownAndAFileWithOnlyErrorsSaysSo()
    {
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(H(), "p"));
        b.Accept(new ToolCall(H(), "c1", "Read", ToolKind.Read, "", "{}") { Target = A });
        b.Accept(new ToolResult(H(), "c1", true, "does not exist"));
        KnowledgeGraph g = b.Snapshot();
        NodeDetail d = NodeDetail.Of(g, A, Root)!;
        Assert.Equal("errors only", Field(d, "State"));
        Assert.Equal("1", Field(d, "Errors"));
        Assert.Empty(KnowledgeView.Of(g, false, 10).Nodes); // Knowledge does not draw it
        Assert.Null(Field(NodeDetail.Of(BuildGraph(), A, Root)!, "Errors"));
    }

    [Fact]
    public void SpanishTextsAreUnchanged()
    {
        using var _ = CultureScope.Spanish();
        NodeDetail b = NodeDetail.Of(BuildGraph(), B, Root)!;
        Assert.Equal("modificado", Field(b, "Estado")); // lang-ok
        Assert.Equal("1 de 1", Field(b, "Turnos")); // lang-ok
        Assert.Equal("aquel se leyó antes de editar este", b.Related[0].Relation); // lang-ok
        NodeDetail e = NodeDetail.Of(BuildGraph(), Root.Combine("ext.txt"), Root)!;
        Assert.Equal("creado (externo)", Field(e, "Estado")); // lang-ok
        Assert.Contains("no los hizo el agente", Field(e, "Cambios externos")); // lang-ok
    }

    [Fact]
    public void TheDetailIsAValue()
    {
        Assert.Equal(NodeDetail.Of(BuildGraph(), B, Root), NodeDetail.Of(BuildGraph(), B, Root));
    }
}
