using System.Globalization;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests;

public sealed class ChangeCorrelatorTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T10:00:00Z", CultureInfo.InvariantCulture);
    private static readonly FilePath File = new(Path.Combine("project", "a.txt"));

    private readonly ChangeCorrelator _correlator = new();

    private static EventHeader At(long seconds, string? subagent) =>
        new(new SessionId("s"), "test", subagent, T0.AddSeconds(seconds), new SourceRef(new FilePath("t"), 1));

    private void Call(string id, string tool, long start, long? end) => Call(id, tool, start, end, null);

    private void Call(string id, string tool, long start, long? end, string? subagent)
    {
        _correlator.Accept(new ToolCall(At(start, subagent), id, tool, "{}"));
        if (end != null) _correlator.Accept(new ToolResult(At(end.Value, subagent), id, false, "ok"));
    }

    private static FileChange ChangedAt(long seconds) => new(File, ChangeKind.Modified, T0.AddSeconds(seconds));

    private static List<string> Ids(Attribution a) => a.Calls.Select(c => c.CallId).ToList();

    [Fact]
    public void ExactWhenTheTraceSaysACallWroteThatFileAndTheTimeMatches()
    {
        Call("w1", "Write", 10, 11L);
        _correlator.Accept(new FileChanged(At(11, null), File, ChangeKind.Created, "w1"));
        Attribution a = _correlator.Attribute(ChangedAt(10));
        Assert.Equal(Confidence.Exact, a.Confidence);
        Assert.Equal(["w1"], Ids(a));
    }

    [Fact]
    public void ALaterExternalEditIsNotAttributedToTheEarlierWrite()
    {
        Call("w1", "Write", 10, 11L);
        _correlator.Accept(new FileChanged(At(11, null), File, ChangeKind.Created, "w1"));
        Attribution a = _correlator.Attribute(ChangedAt(600));
        Assert.Equal(Confidence.None, a.Confidence);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void ProbableWhenExactlyOneCallWasRunning()
    {
        Call("b1", "Bash", 0, 10L);
        Attribution a = _correlator.Attribute(ChangedAt(5));
        Assert.Equal(Confidence.Probable, a.Confidence);
        Assert.Equal(["b1"], Ids(a));
    }

    [Fact]
    public void AmbiguousWhenSeveralCallsOverlap()
    {
        Call("b1", "Bash", 0, 10L);
        Call("b2", "Bash", 3, 8L);
        Attribution a = _correlator.Attribute(ChangedAt(5));
        Assert.Equal(Confidence.Ambiguous, a.Confidence);
        Assert.Equal(["b1", "b2"], Ids(a));
    }

    [Fact]
    public void TheCallThatLaunchesASubagentDoesNotMakeEverythingAmbiguous()
    {
        Call("agent", "Agent", 0, 300L);
        Call("sub-bash", "Bash", 60, 65L, "abc");
        Attribution a = _correlator.Attribute(ChangedAt(63));
        Assert.Equal(Confidence.Probable, a.Confidence);
        Assert.Equal(["sub-bash"], Ids(a));
        // Outside what the subagent does, what is in progress is the call that launched it
        Assert.Equal(["agent"], Ids(_correlator.Attribute(ChangedAt(200))));
    }

    [Fact]
    public void ACallDeclaredToHaveWrittenAnotherFileCannotHaveCausedThisChange()
    {
        var other = new FilePath(Path.Combine("project", "other.txt"));
        Call("b1", "Bash", 0, 10L);
        Call("w1", "Write", 2, 6L);
        _correlator.Accept(new FileChanged(At(6, null), other, ChangeKind.Created, "w1"));
        Attribution a = _correlator.Attribute(ChangedAt(4));
        Assert.Equal(Confidence.Probable, a.Confidence);
        Assert.Equal(["b1"], Ids(a));
        // And the other file is still its own
        Assert.Equal(["w1"], Ids(_correlator.Attribute(new FileChange(other, ChangeKind.Created, T0.AddSeconds(6)))));
    }

    [Fact]
    public void AWriterWhoseResultHasNotArrivedYetStaysAsACandidate()
    {
        Call("b1", "Bash", 0, 10L);
        Call("w1", "Write", 2, null);
        Assert.Equal(Confidence.Ambiguous, _correlator.Attribute(ChangedAt(4)).Confidence);
    }

    [Fact]
    public void ACallWithoutResultYetIsStillRunning()
    {
        Call("b1", "Bash", 0, null);
        Assert.Equal(Confidence.Probable, _correlator.Attribute(ChangedAt(500)).Confidence);
    }

    [Fact]
    public void ThereIsATwoSecondGraceAfterTheResult()
    {
        Call("b1", "Bash", 0, 10L);
        Assert.Equal(Confidence.Probable, _correlator.Attribute(ChangedAt(12)).Confidence);
        Assert.Equal(Confidence.None, _correlator.Attribute(ChangedAt(13)).Confidence);
    }

    [Fact]
    public void AChangeBeforeAnyCallHasNoAttribution()
    {
        Call("b1", "Bash", 20, 30L);
        Assert.Equal(Confidence.None, _correlator.Attribute(ChangedAt(5)).Confidence);
    }

    [Fact]
    public void ADeletedFileHasNoTimeToCorrelate()
    {
        Call("b1", "Bash", 0, 10L);
        Attribution a = _correlator.Attribute(new FileChange(File, ChangeKind.Deleted, null));
        Assert.Equal(Confidence.Undated, a.Confidence);
    }

    [Fact]
    public void EventsWithoutTimestampAreIgnoredAndNeverBreakAnything()
    {
        var noTime = new EventHeader(new SessionId("s"), "test", null, null, new SourceRef(new FilePath("t"), 1));
        _correlator.Accept(new ToolCall(noTime, "x", "Bash", "{}"));
        _correlator.Accept(new ToolResult(noTime, "x", false, ""));
        _correlator.Accept(new FileChanged(noTime, File, ChangeKind.Modified, "x"));
        Assert.Equal(Confidence.None, _correlator.Attribute(ChangedAt(1)).Confidence);
    }

    [Fact]
    public void ResetForgetsThePreviousSession()
    {
        Call("b1", "Bash", 0, 10L);
        _correlator.Reset();
        Assert.Equal(Confidence.None, _correlator.Attribute(ChangedAt(5)).Confidence);
    }

    [Fact]
    public void PathsCompareCaseInsensitivelyWhereTheFileSystemDoes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only applies on file systems that ignore case");
        Call("w1", "Write", 10, 11L);
        _correlator.Accept(new FileChanged(At(11, null), new FilePath(File.Value.ToUpperInvariant()), ChangeKind.Created, "w1"));
        Assert.Equal(Confidence.Exact, _correlator.Attribute(ChangedAt(10)).Confidence);
    }

    // New compared to Java

    [Fact]
    public void AcceptsEveryEventVariantWithoutFailing()
    {
        foreach (AgentEvent e in EventSamples.OfEveryVariant()) _correlator.Accept(e);
    }

    [Fact]
    public void CallsThatStartAtTheSameTimeAreListedInArrivalOrder()
    {
        Call("first", "Bash", 0, 10L);
        Call("second", "Bash", 0, 10L);
        Call("third", "Bash", 0, 10L);

        Assert.Equal(["first", "second", "third"], Ids(_correlator.Attribute(ChangedAt(5))));
    }
}
