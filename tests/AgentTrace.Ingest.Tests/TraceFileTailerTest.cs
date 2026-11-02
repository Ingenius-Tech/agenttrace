using AgentTrace.Testing;
using System.Text;
using AgentTrace.Core;

namespace AgentTrace.Ingest.Tests;

public sealed class TraceFileTailerTest : TempDirTestBase
{
    private static void Append(FilePath f, byte[] bytes)
    {
        using var stream = new FileStream(f.Value, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes);
    }

    private static void Append(FilePath f, string s) => Append(f, Encoding.UTF8.GetBytes(s));

    private static List<string> Texts(IReadOnlyList<RawLine> lines) => [.. lines.Select(l => l.Text)];

    [Fact]
    public void MissingFileYieldsNothingAndIsNotCreated()
    {
        FilePath f = Dir.Combine("missing.jsonl");
        Assert.Empty(new TraceFileTailer(f).Poll());
        Assert.False(File.Exists(f.Value));
    }

    [Fact]
    public void FirstPollReadsExistingContentThenOnlyNewLines()
    {
        FilePath f = Dir.Combine("t.jsonl");
        Append(f, "a\nb\n");
        var t = new TraceFileTailer(f);
        IReadOnlyList<RawLine> first = t.Poll();
        Assert.Equal(["a", "b"], Texts(first));
        Assert.Equal(1, first[0].Number);
        Assert.Equal(2, first[1].Number);
        Assert.Empty(t.Poll());
        Append(f, "c\n");
        IReadOnlyList<RawLine> next = t.Poll();
        Assert.Equal(["c"], Texts(next));
        Assert.Equal(3, next[0].Number);
    }

    [Fact]
    public void PartialLineIsHeldUntilCompleted()
    {
        FilePath f = Dir.Combine("t.jsonl");
        Append(f, "one\ntw");
        var t = new TraceFileTailer(f);
        Assert.Equal(["one"], Texts(t.Poll()));
        Append(f, "o\n");
        Assert.Equal(["two"], Texts(t.Poll()));
    }

    [Fact]
    public void MultibyteCharacterSplitAcrossPollsIsDecodedCorrectly()
    {
        FilePath f = Dir.Combine("t.jsonl");
        byte[] enie = Encoding.UTF8.GetBytes("ñ"); // lang-ok
        Assert.Equal(2, enie.Length);
        Append(f, [enie[0]]);
        var t = new TraceFileTailer(f);
        Assert.Empty(t.Poll());
        Append(f, [enie[1], (byte)'\n']);
        Assert.Equal(["ñ"], Texts(t.Poll())); // lang-ok
    }

    [Fact]
    public void StripsCarriageReturn()
    {
        FilePath f = Dir.Combine("t.jsonl");
        Append(f, "a\r\nb\r\n");
        Assert.Equal(["a", "b"], Texts(new TraceFileTailer(f).Poll()));
    }

    [Fact]
    public void TruncatedFileRestartsFromTheBeginning()
    {
        FilePath f = Dir.Combine("t.jsonl");
        Append(f, "long-1\nlong-2\n");
        var t = new TraceFileTailer(f);
        t.Poll();
        File.WriteAllText(f.Value, "x\n");
        Assert.Equal(["x"], Texts(t.Poll()));
        Assert.True(t.Truncated);
        t.Poll();
        Assert.False(t.Truncated);
    }

    [Fact]
    public void FlushPendingReturnsTrailingLineWithoutNewline()
    {
        FilePath f = Dir.Combine("t.jsonl");
        Append(f, "a\nno-newline");
        var t = new TraceFileTailer(f);
        Assert.Equal(["a"], Texts(t.Poll()));
        RawLine? last = t.FlushPending();
        Assert.NotNull(last);
        Assert.Equal("no-newline", last.Text);
        Assert.Equal(2, last.Number);
        Assert.Null(t.FlushPending());
    }

    [Fact]
    public void ReadsFilesLargerThanTheChunkSize()
    {
        FilePath f = Dir.Combine("big.jsonl");
        string linea = new('x', 1000);
        var sb = new StringBuilder();
        for (int i = 0; i < 3000; i++) sb.Append(linea).Append('\n');
        Append(f, sb.ToString());
        IReadOnlyList<RawLine> lines = new TraceFileTailer(f).Poll();
        Assert.Equal(3000, lines.Count);
        Assert.All(lines, l => Assert.Equal(1000, l.Text.Length));
    }
}
