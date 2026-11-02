using AgentTrace.Testing;
using System.Text;
using AgentTrace.Core;

namespace AgentTrace.Ingest.Tests;

public sealed class FileContentReaderTest : TempDirTestBase
{
    private FilePath File_(string name, byte[] content)
    {
        FilePath p = Dir.Combine(name);
        File.WriteAllBytes(p.Value, content);
        return p;
    }

    private FilePath File_(string name, string content, Encoding charset) => File_(name, charset.GetBytes(content));

    private static byte[] Concat(byte[] a, byte[] b) => [.. a, .. b];

    [Fact]
    public void PlainUtf8IsReadLineByLine()
    {
        FileContent c = FileContentReader.Read(File_("a.txt", "uno\ndos ñ\ntres\n", Encoding.UTF8)); // lang-ok
        Assert.True(c.Readable);
        Assert.Equal(["uno", "dos ñ", "tres"], c.Lines); // lang-ok
        Assert.Equal("UTF-8", c.Encoding);
        Assert.Null(c.EncodingNote);
        Assert.Equal(LineEnding.Lf, c.LineEnding);
        Assert.False(c.Truncated);
    }

    [Fact]
    public void AUtf8BomIsDetectedAndNotShownAsAnInvisibleCharacter()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        FileContent c = FileContentReader.Read(File_("bom.txt", Concat(bom, Encoding.UTF8.GetBytes("hello\n"))));
        Assert.Equal(["hello"], c.Lines);
        Assert.Equal(FileContentReader.Utf8BomLabel, c.Encoding);
    }

    [Fact]
    public void Utf16WithBomIsDecodedEvenThoughItContainsNulBytes()
    {
        byte[] le = Concat([0xFF, 0xFE], Encoding.Unicode.GetBytes("línea\nsegunda\n")); // lang-ok
        FileContent c1 = FileContentReader.Read(File_("le.txt", le));
        Assert.True(c1.Readable, "UTF-16 is not binary");
        Assert.Equal(["línea", "segunda"], c1.Lines); // lang-ok
        Assert.Equal("UTF-16 LE", c1.Encoding);
        byte[] be = Concat([0xFE, 0xFF], Encoding.BigEndianUnicode.GetBytes("hello\n"));
        Assert.Equal(["hello"], FileContentReader.Read(File_("be.txt", be)).Lines);
    }

    [Fact]
    public void TextThatIsNotUtf8FallsBackToLatin1WithAWarning()
    {
        FileContent c = FileContentReader.Read(File_("latin.txt", "canción\n", Encoding.Latin1)); // lang-ok
        Assert.Equal(["canción"], c.Lines); // lang-ok
        Assert.Equal("ISO-8859-1", c.Encoding);
        Assert.NotNull(c.EncodingNote);
    }

    [Fact]
    public void LineEndingsAreDetected()
    {
        Assert.Equal(LineEnding.Lf, FileContentReader.Read(File_("lf", "a\nb\n", Encoding.UTF8)).LineEnding);
        Assert.Equal(LineEnding.CrLf, FileContentReader.Read(File_("crlf", "a\r\nb\r\n", Encoding.UTF8)).LineEnding);
        Assert.Equal(LineEnding.Cr, FileContentReader.Read(File_("cr", "a\rb\r", Encoding.UTF8)).LineEnding);
        Assert.Equal(LineEnding.Mixed, FileContentReader.Read(File_("mixed", "a\r\nb\nc\n", Encoding.UTF8)).LineEnding);
        Assert.Equal(LineEnding.None, FileContentReader.Read(File_("none", "a single line", Encoding.UTF8)).LineEnding);
    }

    [Fact]
    public void AllKindsOfLineBreakSplitTheLinesAndTheTrailingOneAddsNone()
    {
        Assert.Equal(["a", "b", "c"], FileContentReader.Read(File_("x", "a\r\nb\rc\n", Encoding.UTF8)).Lines);
        Assert.Equal(["a", "b"], FileContentReader.Read(File_("y", "a\nb", Encoding.UTF8)).Lines);
        Assert.Equal(["a", "", "b"], FileContentReader.Read(File_("z", "a\n\nb\n", Encoding.UTF8)).Lines);
    }

    [Fact]
    public void AnEmptyFileIsReadableAndHasNoLines()
    {
        FileContent c = FileContentReader.Read(File_("empty.txt", []));
        Assert.True(c.Readable);
        Assert.Empty(c.Lines);
        Assert.Equal(0, c.Size);
    }

    [Fact]
    public void ANulByteMeansBinaryAndNothingIsShown()
    {
        FileContent c = FileContentReader.Read(File_("image.bin", [(byte)'P', (byte)'N', (byte)'G', 0, 1, 2, 3]));
        Assert.True(c.Binary);
        Assert.False(c.Readable);
        Assert.Empty(c.Lines);
        Assert.Equal(7, c.Size);
    }

    [Fact]
    public void AFileOverTheLimitIsTruncatedAtALineBreak()
    {
        string line = new string('x', 99) + "\n";
        int lines = FileContentReader.MaxBytes / line.Length + 100;
        var sb = new StringBuilder(lines * line.Length);
        for (int i = 0; i < lines; i++) sb.Append(line);
        FileContent c = FileContentReader.Read(File_("big.txt", sb.ToString(), Encoding.UTF8));
        Assert.True(c.Truncated);
        Assert.True(c.Readable);
        Assert.Equal(FileContentReader.MaxBytes / line.Length, c.Lines.Count); // cut at a line break, with no partial lines
        Assert.All(c.Lines, l => Assert.Equal(99, l.Length));
        Assert.True(c.Size > FileContentReader.MaxBytes);
    }

    [Fact]
    public void AFileExactlyAtTheLimitIsNotTruncated()
    {
        byte[] data = new byte[FileContentReader.MaxBytes];
        Array.Fill(data, (byte)'a');
        FileContent c = FileContentReader.Read(File_("exact.txt", data));
        Assert.False(c.Truncated);
        Assert.Single(c.Lines);
    }

    [Fact]
    public void AMissingFileAndAFolderAreExplainedNotThrown()
    {
        FileContent missing = FileContentReader.Read(Dir.Combine("missing.txt"));
        Assert.False(missing.Readable);
        Assert.Equal(ContentNote.Missing, missing.Problem);
        FilePath folder = Dir.Combine("folder");
        Directory.CreateDirectory(folder.Value);
        Assert.Equal(ContentNote.IsFolder, FileContentReader.Read(folder).Problem);
    }

    [Fact]
    public void ASymbolicLinkIsOpenedAndTheTargetIsReported()
    {
        FilePath target = File_("real.txt", "content\n", Encoding.UTF8);
        FilePath link = Dir.Combine("link.txt");
        try
        {
            File.CreateSymbolicLink(link.Value, target.Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("Cannot create symbolic links here: " + e.Message);
        }
        FileContent c = FileContentReader.Read(link);
        Assert.Equal(["content"], c.Lines);
        Assert.Equal(target.Value, c.LinkTarget);
    }

    [Fact]
    public void ReadingWhileAnotherProcessRewritesTheFileNeverThrowsAndAlwaysReturnsSomethingCoherent()
    {
        FilePath p = File_("live.txt", "initial\n", Encoding.UTF8);
        bool stop = false;
        var writer = new Thread(() =>
        {
            int i = 0;
            while (!Volatile.Read(ref stop))
            {
                try
                {
                    File.WriteAllText(p.Value, "version " + i++ + "\nsecond line\n");
                }
                catch (IOException)
                {
                    // the reader may have it open for a moment on some systems
                }
            }
        });
        writer.Start();
        try
        {
            for (int i = 0; i < 300; i++)
            {
                FileContent c = FileContentReader.Read(p);
                Assert.NotNull(c);
                Assert.True(c.Readable || c.Problem is not null, "either there is text or there is a reason: " + c);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Join();
        }
    }
}
