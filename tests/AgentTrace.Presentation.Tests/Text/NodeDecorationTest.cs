using AgentTrace.Core;
using AgentTrace.Testing;
using AgentTrace.Presentation.Text;
using AgentTrace.Presentation.Tree;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class NodeDecorationTest
{
    private static readonly FilePath Root = new("project");
    private static readonly EventHeader H = new(new SessionId("s"), "t", null, null, new SourceRef(new FilePath("x"), 1));

    private static FilePath P(string relative) => Root.Combine(relative);

    private static ChangeEntry Change(string file, ChangeKind kind, Confidence confidence)
    {
        Attribution attribution = confidence switch
        {
            Confidence.None => Attribution.None(),
            Confidence.Undated => Attribution.Undated(),
            _ => new Attribution(confidence, [new ToolCall(H, "c", "Bash", "{}")]),
        };
        return new ChangeEntry(new FileChange(P(file), kind, kind == ChangeKind.Deleted ? null : DateTimeOffset.UnixEpoch), attribution);
    }

    private static FileNode File(string relative, bool ghost) => new(P(relative), false, ghost, false);

    private static FileNode Folder(string relative) => new(P(relative), true, false, false);

    private static FileStatusIndex Index(IEnumerable<FilePath> reads, params ChangeEntry[] changes) => FileStatusIndex.Of(Root, changes, reads);

    [Fact]
    public void AnUntouchedFileHasNoDecoration()
    {
        NodeDecoration d = NodeDecoration.Of(File("a.txt", false), Index([]));
        Assert.Equal(NodeDecoration.None, d);
        Assert.False(d.HasChange);
        Assert.Equal("", d.Badge);
    }

    [Fact]
    public void AFileCreatedByTheAgentIsGreenWithAFilledBadgeAndExplainsWhy()
    {
        NodeDecoration d = NodeDecoration.Of(File("a.txt", false), Index([], Change("a.txt", ChangeKind.Created, Confidence.Exact)));
        Assert.Equal(ChangeKind.Created, d.Kind);
        Assert.Equal(Actor.Agent, d.Actor);
        Assert.Equal("+", d.Badge);
        Assert.False(d.Strike);
        Assert.Equal("Created · by the agent", d.Tooltip);
    }

    [Fact]
    public void AnExternalChangeIsMarkedAsExternal()
    {
        NodeDecoration d = NodeDecoration.Of(File("a.txt", false), Index([], Change("a.txt", ChangeKind.Modified, Confidence.None)));
        Assert.Equal(Actor.External, d.Actor);
        Assert.Equal("~", d.Badge);
        Assert.Equal("Modified · external (user or other process)", d.Tooltip);
    }

    [Fact]
    public void ADeletedGhostIsStruckThroughAndItsAuthorIsUnknown()
    {
        NodeDecoration d = NodeDecoration.Of(File("b.txt", true), Index([], Change("b.txt", ChangeKind.Deleted, Confidence.Undated)));
        Assert.Equal(ChangeKind.Deleted, d.Kind);
        Assert.Equal(Actor.Unknown, d.Actor);
        Assert.True(d.Strike);
        Assert.Equal("−", d.Badge);
        Assert.Equal("Deleted · unknown author", d.Tooltip);
    }

    [Fact]
    public void AFileOnlyReadHasTheEyeButNoChange()
    {
        NodeDecoration d = NodeDecoration.Of(File("read.txt", false), Index([P("read.txt")]));
        Assert.True(d.Read);
        Assert.False(d.HasChange);
        Assert.Equal("Read by the agent", d.Tooltip);
    }

    [Fact]
    public void AChangedAndReadFileShowsBoth()
    {
        NodeDecoration d = NodeDecoration.Of(File("a.txt", false),
            Index([P("a.txt")], Change("a.txt", ChangeKind.Modified, Confidence.Probable)));
        Assert.True(d.Read);
        Assert.Equal(ChangeKind.Modified, d.Kind);
        Assert.Equal("Modified · by the agent · read by the agent", d.Tooltip);
    }

    [Fact]
    public void AFolderSummarisesWhatItContainsWithTheStrongestKindAndACount()
    {
        FileStatusIndex idx = Index([],
            Change("src/A.java", ChangeKind.Modified, Confidence.Exact),
            Change("src/B.java", ChangeKind.Created, Confidence.Exact),
            Change("src/C.java", ChangeKind.Modified, Confidence.None));
        NodeDecoration d = NodeDecoration.Of(Folder("src"), idx);
        Assert.Equal(ChangeKind.Modified, d.Kind);
        Assert.Equal(3, d.Count);
        Assert.Equal("~ 3", d.Badge);
        Assert.Equal(Actor.External, d.Actor);
        Assert.Equal("3 changes: 1 created, 2 modified · 1 external", d.Tooltip);
        Assert.False(d.Strike, "a folder that contains a deletion is not struck through");
    }

    [Fact]
    public void AFolderWithOnlyAgentChangesIsFilledAndADeletionWinsTheColour()
    {
        FileStatusIndex idx = Index([],
            Change("src/A.java", ChangeKind.Created, Confidence.Exact),
            Change("src/B.java", ChangeKind.Deleted, Confidence.Undated));
        NodeDecoration d = NodeDecoration.Of(Folder("src"), idx);
        Assert.Equal(ChangeKind.Deleted, d.Kind);
        Assert.Equal(Actor.Agent, d.Actor);
        Assert.Equal("− 2", d.Badge);
        Assert.False(d.Strike);
    }

    [Fact]
    public void AFolderWithASingleChangeUsesTheSingularAndNoCount()
    {
        NodeDecoration d = NodeDecoration.Of(Folder("src"), Index([], Change("src/A.java", ChangeKind.Created, Confidence.Exact)));
        Assert.Equal("+", d.Badge);
        Assert.True(d.Tooltip!.StartsWith("1 change: ", StringComparison.Ordinal), d.Tooltip);
    }

    [Fact]
    public void TooltipsAreShownInSpanish()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("Creado · por el agente", // lang-ok
            NodeDecoration.Of(File("a.txt", false), Index([], Change("a.txt", ChangeKind.Created, Confidence.Exact))).Tooltip);
        Assert.Equal("Leído por el agente", NodeDecoration.Of(File("r.txt", false), Index([P("r.txt")])).Tooltip); // lang-ok
        FileStatusIndex idx = Index([],
            Change("src/A.java", ChangeKind.Modified, Confidence.Exact),
            Change("src/B.java", ChangeKind.Created, Confidence.Exact),
            Change("src/C.java", ChangeKind.Modified, Confidence.None));
        Assert.Equal("3 cambios: 1 creados, 2 modificados · 1 externo", NodeDecoration.Of(Folder("src"), idx).Tooltip); // lang-ok
    }

    [Fact]
    public void AFolderWithoutChangesHasNoDecoration()
    {
        Assert.Equal(NodeDecoration.None, NodeDecoration.Of(Folder("docs"), Index([], Change("src/A.java", ChangeKind.Created, Confidence.Exact))));
        Assert.Null(NodeDecoration.Of(Folder("docs"), Index([])).Tooltip);
    }

    [Fact]
    public void AGhostFolderIsStruckThrough()
    {
        var ghostFolder = new FileNode(P("old"), true, true, false);
        NodeDecoration d = NodeDecoration.Of(ghostFolder, Index([], Change("old/A.java", ChangeKind.Deleted, Confidence.Undated)));
        Assert.Equal(ChangeKind.Deleted, d.Kind);
        Assert.True(d.Strike);
    }
}
