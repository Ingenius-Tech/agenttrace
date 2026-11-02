using AgentTrace.Core;
using AgentTrace.Presentation.Text;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Text.StatusSegments;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class StatusSegmentsTest
{
    private static readonly EventHeader H = new(new SessionId("s"), "test", null, null, new SourceRef(new FilePath("t"), 1));

    private static Segment? Find(IReadOnlyList<Segment> segments, SegmentKind kind) => segments.FirstOrDefault(s => s.Kind == kind);

    private static SessionStats Stats(params AgentEvent[] events)
    {
        var s = new SessionStats();
        foreach (AgentEvent e in events) s.Accept(e);
        return s;
    }

    [Fact]
    public void CompactsBigNumbersInEnglishNotation()
    {
        Assert.Equal("0", Compact(0));
        Assert.Equal("950", Compact(950));
        Assert.Equal("1.0 K", Compact(1_000));
        Assert.Equal("4.9 K", Compact(4_932));
        Assert.Equal("846.9 K", Compact(846_854));
        Assert.Equal("1.2 M", Compact(1_234_567));
        Assert.Equal("163.2 M", Compact(163_241_911));
        Assert.Equal("2.0 G", Compact(2_000_000_000L));
        Assert.Equal("1,234,567", Full(1_234_567));
        Assert.Equal("12", Full(12));
    }

    [Fact]
    public void AnEmptySessionShowsZeroesAndNoErrorsInGreen()
    {
        IReadOnlyList<Segment> s = Of(new SessionStats());
        Assert.Equal("0", Find(s, SegmentKind.Events)!.Value);
        Assert.Equal(Tone.Ok, Find(s, SegmentKind.Errors)!.Tone);
        Assert.Equal("0 → 0", Find(s, SegmentKind.Tokens)!.Value);
        Assert.Null(Find(s, SegmentKind.External));
        Assert.Null(Find(s, SegmentKind.Subagents));
    }

    [Fact]
    public void ErrorsTurnRedAndEveryCounterKeepsItsLabel()
    {
        SessionStats st = Stats(new ToolCall(H, "1", "Bash", "{}"), new ToolCall(H, "2", "Bash", "{}"),
            new ToolResult(H, "1", true, "boom"), new ToolResult(H, "2", false, "ok"),
            new FileChanged(H, new FilePath("a"), ChangeKind.Created, "1"), new FileChanged(H, new FilePath("b"), ChangeKind.Modified, null),
            new SubagentStarted(H, "1", "abc", "x"), new TurnEnded(H, null),
            new TokenUsage(H, "m", 1_500, 2_000_000, 3, 4));
        IReadOnlyList<Segment> s = Of(st);
        Assert.Equal("1", Find(s, SegmentKind.Errors)!.Value);
        Assert.Equal("error", Find(s, SegmentKind.Errors)!.Label);
        Assert.Equal(Tone.Error, Find(s, SegmentKind.Errors)!.Tone);
        Assert.Equal("2", Find(s, SegmentKind.Files)!.Value);
        Assert.Equal("1", Find(s, SegmentKind.External)!.Value);
        Assert.Equal("subagent", Find(s, SegmentKind.Subagents)!.Label);
        Assert.Equal("turn", Find(s, SegmentKind.Turns)!.Label);
        Assert.Equal("1.5 K → 2.0 M", Find(s, SegmentKind.Tokens)!.Value);
        Assert.True(Find(s, SegmentKind.Tokens)!.Tooltip.Contains("1,500", StringComparison.Ordinal)
            && Find(s, SegmentKind.Tokens)!.Tooltip.Contains("2,000,000", StringComparison.Ordinal));
        Assert.All(s, x => Assert.True(!string.IsNullOrWhiteSpace(x.Label) && !string.IsNullOrWhiteSpace(x.Value)));
    }

    [Fact]
    public void TheOrderIsStable()
    {
        List<SegmentKind> kinds = Of(new SessionStats()).Select(x => x.Kind).ToList();
        Assert.Equal<SegmentKind>(
            [SegmentKind.Events, SegmentKind.Turns, SegmentKind.Tools, SegmentKind.Errors, SegmentKind.Files, SegmentKind.Tokens], kinds);
    }

    /// <summary>Added case: the half goes up (like Java's String.format HALF_UP) and does not depend on the culture.</summary>
    [Theory]
    [InlineData(1_050, "1.1 K")]
    [InlineData(1_150, "1.2 K")]
    [InlineData(1_049, "1.0 K")]
    [InlineData(999_950, "1000.0 K")]
    [InlineData(1_050_000, "1.1 M")]
    public void RoundsHalfUp(long n, string expected)
    {
        Assert.Equal(expected, Compact(n));
    }

    [Fact]
    public void NumbersAndLabelsAreShownInSpanish()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("4,9 K", Compact(4_932));
        Assert.Equal("1.234.567", Full(1_234_567));
        Assert.Equal("1,1 K", Compact(1_050));
        SessionStats st = Stats(new ToolCall(H, "1", "Bash", "{}"), new ToolResult(H, "1", true, "boom"),
            new SubagentStarted(H, "1", "abc", "x"), new TurnEnded(H, null));
        IReadOnlyList<Segment> s = Of(st);
        Assert.Equal("eventos", Find(s, SegmentKind.Events)!.Label); // lang-ok
        Assert.Equal("turno", Find(s, SegmentKind.Turns)!.Label); // lang-ok
        Assert.Equal("error", Find(s, SegmentKind.Errors)!.Label); // lang-ok
        Assert.Equal("subagente", Find(s, SegmentKind.Subagents)!.Label); // lang-ok
        Assert.Equal("Llamadas a herramientas que devolvieron un error", Find(s, SegmentKind.Errors)!.Tooltip); // lang-ok
        Assert.Equal("errores", Of(new SessionStats()).First(x => x.Kind == SegmentKind.Errors).Label); // lang-ok
    }
}
