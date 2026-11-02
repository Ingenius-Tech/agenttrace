using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core.Tests;

public sealed class SessionStatsTest
{
    private static readonly EventHeader H =
        new(new SessionId("s1"), "test", null, null, new SourceRef(new FilePath("t.jsonl"), 1));

    [Fact]
    public void EmptyStatsAreZero()
    {
        var s = new SessionStats();
        Assert.Equal(0, s.Events);
        Assert.Equal(0, s.TotalToolCalls);
        Assert.Empty(s.FilesTouched);
        Assert.Equal(TimeSpan.Zero, s.TurnTime);
    }

    [Fact]
    public void SumsTokensAcrossUsageEvents()
    {
        var s = new SessionStats();
        s.Accept(new TokenUsage(H, "m", 10, 20, 30, 40));
        s.Accept(new TokenUsage(H, "m", 1, 2, 3, 4));
        Assert.Equal(11, s.InputTokens);
        Assert.Equal(22, s.OutputTokens);
        Assert.Equal(33, s.CacheReadTokens);
        Assert.Equal(44, s.CacheCreationTokens);
    }

    [Fact]
    public void CountsToolCallsByNameAndErrors()
    {
        var s = new SessionStats();
        s.Accept(new ToolCall(H, "1", "Bash", "{}"));
        s.Accept(new ToolCall(H, "2", "Bash", "{}"));
        s.Accept(new ToolCall(H, "3", "Write", "{}"));
        s.Accept(new ToolResult(H, "1", false, "ok"));
        s.Accept(new ToolResult(H, "2", true, "failure"));
        Assert.Equal(3, s.TotalToolCalls);
        Assert.Equal(2L, s.ToolCalls["Bash"]);
        Assert.Equal(1, s.ToolErrors);
    }

    [Fact]
    public void TracksTouchedFilesAndMarksExternalOnes()
    {
        var s = new SessionStats();
        var a = new FilePath("a.txt");
        var b = new FilePath("b.txt");
        s.Accept(new FileChanged(H, a, ChangeKind.Created, "1"));
        s.Accept(new FileChanged(H, a, ChangeKind.Modified, "2"));
        s.Accept(new FileChanged(H, b, ChangeKind.Modified, null));
        Assert.Equal(2, s.FilesTouched.Count);
        Assert.Single(s.FilesChangedExternally);
        Assert.Contains(b, s.FilesChangedExternally);
    }

    [Fact]
    public void TracksFilesReadSeparatelyFromFilesTouched()
    {
        var s = new SessionStats();
        s.Accept(new FileRead(H, new FilePath("read.txt"), "1"));
        s.Accept(new FileRead(H, new FilePath("read.txt"), "2"));
        s.Accept(new FileChanged(H, new FilePath("a.txt"), ChangeKind.Created, "3"));
        Assert.Equal(new HashSet<FilePath> { new("read.txt") }, s.FilesRead);
        Assert.Equal(new HashSet<FilePath> { new("a.txt") }, s.FilesTouched);
        Assert.Equal(3, s.Events);
    }

    [Fact]
    public void CountsDistinctSubagentsEvenIfTheLinkIsEmittedTwice()
    {
        var s = new SessionStats();
        s.Accept(new SubagentStarted(H, "call-1", "abc", "investigate"));
        s.Accept(new SubagentStarted(H, "call-1", "abc", "investigate"));
        s.Accept(new SubagentStarted(H, "call-2", "def", "review"));
        Assert.Equal(new HashSet<string> { "abc", "def" }, s.Subagents);
    }

    [Fact]
    public void AccumulatesTurnsAndToleratesMissingDuration()
    {
        var s = new SessionStats();
        s.Accept(new TurnEnded(H, TimeSpan.FromSeconds(3)));
        s.Accept(new TurnEnded(H, null));
        Assert.Equal(2, s.Turns);
        Assert.Equal(TimeSpan.FromSeconds(3), s.TurnTime);
    }

    [Fact]
    public void CountsSkippedByReasonAndAllEvents()
    {
        var s = new SessionStats();
        s.Accept(new Skipped(H, "mode", SkipReason.Ignored));
        s.Accept(new Skipped(H, "x", SkipReason.Unknown));
        s.Accept(new Skipped(H, "x", SkipReason.Unknown));
        s.Accept(new UserMessage(H, "hello"));
        Assert.Equal(1, s.SkippedCount(SkipReason.Ignored));
        Assert.Equal(2, s.SkippedCount(SkipReason.Unknown));
        Assert.Equal(0, s.SkippedCount(SkipReason.Malformed));
        Assert.Equal(4, s.Events);
    }

    // New with respect to Java (C# does not check switch exhaustiveness)

    [Fact]
    public void AcceptsEveryEventVariantWithoutFailingAndCountsEachOne()
    {
        var s = new SessionStats();
        var all = EventSamples.OfEveryVariant();

        foreach (AgentEvent e in all) s.Accept(e);

        Assert.Equal(all.Count, s.Events);
    }

    [Fact]
    public void ToolCallsAreSortedByNameWithOrdinalComparison()
    {
        var s = new SessionStats();
        foreach (string name in new[] { "write", "Bash", "Write", "Edit" }) s.Accept(new ToolCall(H, name, name, "{}"));

        Assert.Equal(["Bash", "Edit", "Write", "write"], s.ToolCalls.Keys.ToArray());
    }

    [Fact]
    public void ReturnedCollectionsAreCopiesThatLaterEventsDoNotChange()
    {
        var s = new SessionStats();
        s.Accept(new FileRead(H, new FilePath("a.txt"), "1"));
        var before = s.FilesRead;

        s.Accept(new FileRead(H, new FilePath("b.txt"), "2"));

        Assert.Single(before);
        Assert.Equal(2, s.FilesRead.Count);
    }
}
