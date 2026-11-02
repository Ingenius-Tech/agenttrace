using AgentTrace.Core;
using AgentTrace.Core.Search;
using AgentTrace.Presentation.Search;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Search;

public sealed class SearchPresentationTest
{
    private static readonly FilePath File = new(OperatingSystem.IsWindows() ? @"C:\p\a.cs" : "/p/a.cs");

    private static SearchEntry Entry(SearchKind kind, string? callId = null, FilePath? path = null, string? lane = null, bool truncated = false) =>
        new(kind, "text", callId, 4, lane, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), path, truncated);

    [Fact]
    public void AFileGoesToTheViewerAToolToItsStepAndAMessageToItsTurn()
    {
        Assert.Equal(new JumpTarget.File(File), JumpTarget.Of(Entry(SearchKind.File, "c1", File)));
        Assert.Equal(new JumpTarget.Step("c1"), JumpTarget.Of(Entry(SearchKind.Tool, "c1")));
        Assert.Equal(new JumpTarget.Step("c1"), JumpTarget.Of(Entry(SearchKind.Error, "c1")));
        Assert.Equal(new JumpTarget.Turn(4), JumpTarget.Of(Entry(SearchKind.Response)));
        Assert.Equal(new JumpTarget.Turn(4), JumpTarget.Of(Entry(SearchKind.Request)));
        Assert.Equal(new JumpTarget.Turn(4), JumpTarget.Of(Entry(SearchKind.Thinking)));
    }

    [Fact]
    public void AToolResultWithoutItsCallFallsBackToTheTurn()
    {
        Assert.Equal(new JumpTarget.Turn(4), JumpTarget.Of(Entry(SearchKind.Tool)));
    }

    [Fact]
    public void EveryKindHasALabelAndTheOriginIsReadable()
    {
        foreach (SearchKind kind in Enum.GetValues<SearchKind>()) Assert.NotEmpty(SearchPresentation.KindLabel(kind));
        string where = SearchPresentation.Where(Entry(SearchKind.Tool, lane: "abcdef0123456789", truncated: true));
        Assert.Contains("Turn 5", where);
        Assert.Contains("subagent abcdef01", where);
        Assert.Contains("text cut off", where);
        Assert.Contains("main thread", SearchPresentation.Where(Entry(SearchKind.Tool)));
    }

    [Fact]
    public void TextsAreShownInSpanish()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("Petición", SearchPresentation.KindLabel(SearchKind.Request)); // lang-ok
        string where = SearchPresentation.Where(Entry(SearchKind.Tool, lane: "abcdef0123456789", truncated: true));
        Assert.Contains("Turno 5", where); // lang-ok
        Assert.Contains("subagente abcdef01", where); // lang-ok
        Assert.Contains("texto cortado", where); // lang-ok
        Assert.Equal("Sin resultados", SearchPresentation.Summary(0, 500)); // lang-ok
        Assert.Equal("1 resultado", SearchPresentation.Summary(1, 500)); // lang-ok
        Assert.Equal("7 resultados", SearchPresentation.Summary(7, 500)); // lang-ok
        Assert.Contains("hay más", SearchPresentation.Summary(500, 500)); // lang-ok
    }

    [Fact]
    public void TheSummaryCountsAndWarnsAtTheLimit()
    {
        Assert.Equal("No results", SearchPresentation.Summary(0, 500));
        Assert.Equal("1 result", SearchPresentation.Summary(1, 500));
        Assert.Equal("7 results", SearchPresentation.Summary(7, 500));
        Assert.Contains("there are more", SearchPresentation.Summary(500, 500));
    }
}
