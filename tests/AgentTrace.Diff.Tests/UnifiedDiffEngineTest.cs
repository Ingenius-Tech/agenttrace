using System.Diagnostics;
using AgentTrace.Diff;

namespace AgentTrace.Diff.Tests;

public sealed class UnifiedDiffEngineTest
{
    private static DiffResult Diff(string? a, string? b) => UnifiedDiffEngine.ComputeDiff(a, b, "f.txt");

    private static string Lines(int from, int to) =>
        string.Join("\n", Enumerable.Range(from, to - from + 1).Select(i => "l" + i)) + "\n";

    [Fact]
    public void IdenticalTextsHaveNoChanges()
    {
        var r = Diff("a\nb\n", "a\nb\n");
        Assert.Equal(0, r.Additions);
        Assert.Equal(0, r.Deletions);
        Assert.Empty(r.Hunks);
        Assert.Equal(2, r.AllLines.Count);
        Assert.All(r.AllLines, l => Assert.Equal(LineType.Unchanged, l.Type));
    }

    [Fact]
    public void ReportsAdditionDeletionAndChangeWithLineNumbers()
    {
        var r = Diff("a\nb\nc\n", "a\nB\nc\nd\n");
        Assert.Equal(2, r.Additions);
        Assert.Equal(1, r.Deletions);
        var all = r.AllLines;
        Assert.Equal(LineType.Deletion, all[1].Type);
        Assert.Equal("b", all[1].Content);
        Assert.Equal(2, all[1].OldNumber);
        Assert.Null(all[1].NewNumber);
        Assert.Equal(LineType.Addition, all[2].Type);
        Assert.Equal("B", all[2].Content);
        Assert.Equal(2, all[2].NewNumber);
        Assert.Equal("d", all[4].Content);
        Assert.Equal(4, all[4].NewNumber);
    }

    [Fact]
    public void NullSideMeansTheFileDidNotExist()
    {
        var created = Diff(null, "x\ny\n");
        Assert.True(created.IsNewFile);
        Assert.Equal(2, created.Additions);
        var removed = Diff("x\ny\n", null);
        Assert.True(removed.IsDeleted);
        Assert.Equal(2, removed.Deletions);
    }

    [Fact]
    public void LineEndingOnlyChangesProduceNoDifferences()
    {
        var r = Diff("a\r\nb\r\n", "a\nb\n");
        Assert.Equal(0, r.Additions + r.Deletions);
        Assert.Empty(r.Hunks);
    }

    [Fact]
    public void TrailingNewlineDoesNotCreateAnEmptyLine()
    {
        Assert.Equal(2, Diff("a\nb\n", "a\nb\n").AllLines.Count);
        Assert.Equal(2, Diff("a\nb", "a\nb").AllLines.Count);
    }

    [Fact]
    public void HunkCarriesThreeLinesOfContextAndCorrectRanges()
    {
        var before = Lines(1, 20);
        var after = before.Replace("l10\n", "ten\n");
        var r = Diff(before, after);
        Assert.Single(r.Hunks);
        var h = r.Hunks[0];
        Assert.Equal(7, h.OldStart);
        Assert.Equal(7, h.OldCount);
        Assert.Equal(7, h.NewStart);
        Assert.Equal(7, h.NewCount);
        Assert.Equal("l7", h.Lines[0].Content);
        Assert.Equal("l13", h.Lines[^1].Content);
    }

    [Fact]
    public void NearbyChangesShareAHunkAndDistantOnesDoNot()
    {
        var before = Lines(1, 40);
        var near = before.Replace("l10\n", "x\n").Replace("l14\n", "y\n");
        Assert.Single(Diff(before, near).Hunks);
        var far = before.Replace("l5\n", "x\n").Replace("l35\n", "y\n");
        var hunks = Diff(before, far).Hunks;
        Assert.Equal(2, hunks.Count);
        Assert.Equal(32, hunks[1].OldStart);
    }

    [Fact]
    public void ChangeAtTheStartAndEndOfTheFile()
    {
        var before = Lines(1, 10);
        var r = Diff(before, "new\n" + before + "last\n");
        Assert.Equal(2, r.Additions);
        Assert.Equal(2, r.Hunks.Count);
        Assert.Equal(1, r.Hunks[0].OldStart);
    }

    [Fact]
    public void TooManyLinesAreNotDiffed()
    {
        var big = Lines(1, UnifiedDiffEngine.MaxLines + 1);
        var r = Diff(big, big + "x\n");
        Assert.NotNull(r.Unavailable);
        Assert.Empty(r.AllLines);
        Assert.Null(Diff("a\n", "b\n").Unavailable);
    }

    [Fact]
    public void LargeFileWithScatteredChangesIsFast()
    {
        var before = Lines(1, 20_000);
        var after = before;
        for (var i = 100; i <= 19_900; i += 100) after = after.Replace("l" + i + "\n", "changed" + i + "\n");
        var watch = Stopwatch.StartNew();
        var r = Diff(before, after);
        watch.Stop();
        Assert.Equal(199, r.Additions);
        Assert.Equal(199, r.Deletions);
        Assert.NotEmpty(r.Hunks);
        Assert.True(watch.ElapsedMilliseconds < 1000, "took " + watch.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void DetectsBinaryContentLikeGit()
    {
        Assert.True(UnifiedDiffEngine.LooksBinary("a\0b"u8));
        Assert.False(UnifiedDiffEngine.LooksBinary("normal text\n"u8));
        var late = new byte[9_000];
        Array.Fill(late, (byte)'a');
        late[8_500] = 0;
        Assert.False(UnifiedDiffEngine.LooksBinary(late), "a NUL past 8,000 bytes does not count");
    }
}
