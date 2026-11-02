using AgentTrace.Core.Search;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests.Search;

public sealed class SearchIndexTest
{
    private static EventHeader H(string? sub = null) => new(new SessionId("s"), "test", sub, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), new SourceRef(new FilePath("t"), 1));

    private static readonly FilePath Target = new(OperatingSystem.IsWindows() ? @"C:\p\Ñandú.cs" : "/p/Ñandú.cs"); // lang-ok: accent-folding data

    private static List<AgentEvent> Session() =>
    [
        new UserMessage(H(), "Fix the compiler failure"),
        new AssistantText(H(), "I am going to run mvn to see the error"),
        new ToolCall(H(), "c1", "Bash", ToolKind.Execute, "mvn test", "{\"command\":\"mvn test\"}"),
        new ToolResult(H(), "c1", true, "BUILD FAILURE: no se encuentra el símbolo"), // lang-ok
        new FileRead(H(), Target, "c2"),
        new TurnEnded(H(), null),
        new UserMessage(H(), "Thanks, now the second turn"),
        new ToolCall(H("sub1"), "c3", "Grep", ToolKind.Search, "símbolo", "{}"), // lang-ok
        new FileChanged(H(), Target, ChangeKind.Modified, "c4"),
        new TokenUsage(H(), "m", 1, 2, 3, 4),
        new Skipped(H(), "x", SkipReason.Ignored),
    ];

    private static SearchIndex Build(IEnumerable<AgentEvent> events)
    {
        var index = new SearchIndex();
        foreach (AgentEvent e in events) index.Accept(e);
        return index;
    }

    [Fact]
    public void NormalizationLowercasesAndDropsAccentsKeepingTheLength()
    {
        Assert.Equal("simbolo", SearchIndex.Normalize("Símbolo")); // lang-ok
        Assert.Equal("nandu aeiou", SearchIndex.Normalize("Ñandú ÁÉÍÓÚ").Replace("ñ", "n")); // lang-ok
        Assert.Equal("Símbolo".Length, SearchIndex.Normalize("Símbolo").Length); // lang-ok
    }

    [Fact]
    public void SurrogatesAndHalfSurrogatesDoNotBreakTheIndex()
    {
        string emoji = char.ConvertFromUtf32(0x1F600);
        string halfEmoji = "broken " + (char)0xD83D; // a lone half, like the one left by cutting a string in the middle of an emoji
        SearchIndex index = Build([new UserMessage(H(), "p"), new AssistantText(H(), "fine " + emoji + " done " + halfEmoji)]);
        Assert.Single(index.Search(new SearchQuery("done")));
        Assert.Single(index.Search(new SearchQuery(emoji)));
    }

    [Fact]
    public void FindsAnErrorAFileAndATextOfTheResponseIgnoringCaseAndAccents()
    {
        SearchIndex index = Build(Session());
        SearchHit error = Assert.Single(index.Search(new SearchQuery("simbolo", new HashSet<SearchKind> { SearchKind.Error })));
        Assert.Equal("c1", error.Entry.CallId);
        Assert.Contains("BUILD FAILURE", error.Snippet);
        Assert.Equal(2, index.Search(new SearchQuery("ANDU")).Count); // read and change of the file
        Assert.Equal(SearchKind.Response, Assert.Single(index.Search(new SearchQuery("run MVN"))).Entry.Kind);
    }

    [Fact]
    public void AllWordsMustAppearAndNothingFindsNothing()
    {
        SearchIndex index = Build(Session());
        Assert.Empty(index.Search(new SearchQuery("mvn nonexistent")));
        Assert.Empty(index.Search(new SearchQuery("   ")));
    }

    [Fact]
    public void FiltersByLaneAndTurn()
    {
        SearchIndex index = Build(Session());
        IReadOnlyList<SearchHit> sub = index.Search(new SearchQuery("simbolo", Lane: SearchLane.Subagent));
        Assert.Equal("c3", Assert.Single(sub).Entry.CallId);
        Assert.DoesNotContain(index.Search(new SearchQuery("simbolo", Lane: SearchLane.Main)), h => h.Entry.Lane is not null);
        Assert.All(index.Search(new SearchQuery("the", Turn: 1)), h => Assert.Equal(1, h.Entry.Turn));
        Assert.Equal(0, index.Search(new SearchQuery("failure"))[0].Entry.Turn);
    }

    [Fact]
    public void ResultsComeInTimeOrderWithTheirHighlights()
    {
        SearchIndex index = Build(Session());
        IReadOnlyList<SearchHit> hits = index.Search(new SearchQuery("simbolo"));
        Assert.Equal(["c1", "c3"], hits.Select(h => h.Entry.CallId));
        SearchHit hit = hits[0];
        (int start, int length) = Assert.Single(hit.Highlights);
        Assert.Equal("símbolo", hit.Snippet.Substring(start, length)); // lang-ok
    }

    [Fact]
    public void HighlightsStayAlignedWhenTheSnippetIsCutAndHasLineBreaks()
    {
        string text = new string('x', 100) + "\r\nlínea con Símbolo aquí\r\n" + new string('y', 100); // lang-ok
        SearchIndex index = Build([new UserMessage(H(), "p"), new AssistantText(H(), text)]);
        SearchHit hit = Assert.Single(index.Search(new SearchQuery("simbolo")));
        (int start, int length) = Assert.Single(hit.Highlights);
        Assert.Equal("Símbolo", hit.Snippet.Substring(start, length)); // lang-ok
        Assert.StartsWith("…", hit.Snippet);
    }

    [Fact]
    public void ABatchedIndexEqualsTheOneBuiltAtOnce()
    {
        List<AgentEvent> events = Session();
        SearchIndex whole = Build(events);
        var batched = new SearchIndex();
        foreach (AgentEvent[] batch in events.Chunk(3)) foreach (AgentEvent e in batch) batched.Accept(e);
        Assert.Equal(whole.Count, batched.Count);
        Assert.Equal(whole.Search(new SearchQuery("the")).Select(h => h.Entry), batched.Search(new SearchQuery("the")).Select(h => h.Entry));
    }

    [Fact]
    public void LongOutputIsTruncatedAndSaysSo()
    {
        SearchIndex index = Build([new UserMessage(H(), "p"), new ToolResult(H(), "c", false, new string('a', 30_000) + " end")]);
        Assert.Empty(index.Search(new SearchQuery("end")));
        Assert.True(Assert.Single(index.Search(new SearchQuery("aaaa"))).Entry.Truncated);
    }

    [Fact]
    public void ANewResultShowsUpInTheNextSearch()
    {
        SearchIndex index = Build(Session());
        int before = index.Search(new SearchQuery("new")).Count;
        index.Accept(new AssistantText(H(), "something new"));
        Assert.Equal(before + 1, index.Search(new SearchQuery("new")).Count);
    }
}
