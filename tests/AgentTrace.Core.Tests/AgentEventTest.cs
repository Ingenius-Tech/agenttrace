using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests;

public sealed class AgentEventTest
{
    private static readonly EventHeader H = EventSamples.Header;

    [Fact]
    public void ThereIsOneSampleForEveryNestedVariant()
    {
        var variants = typeof(AgentEvent).GetNestedTypes()
            .Where(t => typeof(AgentEvent).IsAssignableFrom(t))
            .ToHashSet();
        var sampled = EventSamples.OfEveryVariant().Select(e => e.GetType()).ToHashSet();

        Assert.Equal(11, variants.Count);
        Assert.True(variants.SetEquals(sampled), "a variant is missing in EventSamples");
    }

    [Fact]
    public void EveryVariantCarriesItsHeader()
    {
        foreach (AgentEvent e in EventSamples.OfEveryVariant()) Assert.Same(H, e.Header);
    }

    [Fact]
    public void EveryVariantIsSealedAndComparesByValue()
    {
        foreach (AgentEvent e in EventSamples.OfEveryVariant())
        {
            Assert.True(e.GetType().IsSealed, e.GetType().Name);
            Assert.Equal(e, e with { });
            Assert.Equal(e.GetHashCode(), (e with { }).GetHashCode());
        }
    }

    [Fact]
    public void ToolCallShortcutUsesOtherKindAndNoSummary()
    {
        var call = new ToolCall(H, "1", "Bash", "{}");

        Assert.Equal(ToolKind.Other, call.Kind);
        Assert.Equal("", call.Summary);
        Assert.Equal(new ToolCall(H, "1", "Bash", ToolKind.Other, "", "{}"), call);
    }

    [Fact]
    public void ToolCallStoresANullSummaryAsEmpty()
    {
        var call = new ToolCall(H, "1", "Bash", ToolKind.Execute, null!, "{}");

        Assert.Equal("", call.Summary);
        Assert.Equal("ls", (call with { Summary = "ls" }).Summary);
    }

    [Fact]
    public void FilePathsInEventsComparePathsNotText()
    {
        var a = new FileChanged(H, new FilePath("x/../a.txt"), ChangeKind.Created, "c");
        var b = new FileChanged(H, new FilePath("a.txt"), ChangeKind.Created, "c");

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Kind = ChangeKind.Deleted });
    }

    [Fact]
    public void FileChangedAndFileReadAcceptNoCallId()
    {
        Assert.Null(new FileChanged(H, new FilePath("a"), ChangeKind.Modified, null).CallId);
        Assert.Null(new FileRead(H, new FilePath("a"), null).CallId);
    }

    [Fact]
    public void DurationsAreOptional()
    {
        Assert.Null(new Thinking(H, "", null).Duration);
        Assert.Null(new TurnEnded(H, null).Duration);
    }

    [Fact]
    public void HeadersCompareByValue()
    {
        var a = new EventHeader(new SessionId("s"), "x", null, DateTimeOffset.UnixEpoch, new SourceRef(new FilePath("t"), 1));
        var b = new EventHeader(new SessionId("s"), "x", null, DateTimeOffset.UnixEpoch, new SourceRef(new FilePath("t"), 1));

        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { SubagentId = "sub" });
    }

    [Fact]
    public void ActorsOfMapsEveryConfidence()
    {
        Assert.Equal(Actor.Agent, Actors.Of(new Attribution(Confidence.Exact, [])));
        Assert.Equal(Actor.Agent, Actors.Of(new Attribution(Confidence.Probable, [])));
        Assert.Equal(Actor.Agent, Actors.Of(new Attribution(Confidence.Ambiguous, [])));
        Assert.Equal(Actor.External, Actors.Of(Attribution.None()));
        Assert.Equal(Actor.Unknown, Actors.Of(Attribution.Undated()));
    }

    [Fact]
    public void FileVersionsUnavailableCarriesTheReasonAndNoText()
    {
        FileVersions v = FileVersions.Unavailable(new FilePath("a.bin"), UnavailableReason.Binary);

        Assert.Equal(UnavailableReason.Binary, v.UnavailableReason);
        Assert.Null(v.Before);
        Assert.Null(v.After);
    }
}
