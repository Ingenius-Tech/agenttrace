using AgentTrace.Core;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Text;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class FileHeaderTest
{
    private static readonly FilePath P = new("a.txt");

    private static FileContent TextContent(IReadOnlyList<string> lines, LineEnding ending, long size, bool truncated, ContentNote? note, string? link) =>
        new(P, [.. lines], "UTF-8", note, ending, truncated, false, size, link, null, null);

    [Fact]
    public void DetailsListEncodingLineEndingLinesAndSize()
    {
        Assert.Equal("UTF-8 · LF · 3 lines · 1.5 KB", FileHeader.Details(TextContent(["a", "b", "c"], LineEnding.Lf, 1536, false, null, null)));
        Assert.Equal("UTF-8 · CRLF · 1 line · 12 B", FileHeader.Details(TextContent(["a"], LineEnding.CrLf, 12, false, null, null)));
        Assert.Equal("UTF-8 · LF · 1,234 lines · 2.0 MB",
            FileHeader.Details(TextContent([.. Enumerable.Repeat("x", 1234)], LineEnding.Lf, 2 * 1024 * 1024, false, null, null)));
    }

    [Fact]
    public void DetailsAreShownInSpanish()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("UTF-8 · LF · 3 líneas · 1,5 KB", FileHeader.Details(TextContent(["a", "b", "c"], LineEnding.Lf, 1536, false, null, null))); // lang-ok
        Assert.Equal("UTF-8 · CRLF · 1 línea · 12 B", FileHeader.Details(TextContent(["a"], LineEnding.CrLf, 12, false, null, null))); // lang-ok
        Assert.Equal("UTF-8 · LF · 1.234 líneas · 2,0 MB", // lang-ok
            FileHeader.Details(TextContent([.. Enumerable.Repeat("x", 1234)], LineEnding.Lf, 2 * 1024 * 1024, false, null, null)));
    }

    [Fact]
    public void SizesUseTheRightUnit()
    {
        Assert.Equal("0 B", FileHeader.Size(0));
        Assert.Equal("1023 B", FileHeader.Size(1023));
        Assert.Equal("1.0 KB", FileHeader.Size(1024));
        Assert.Equal("1.0 MB", FileHeader.Size(1024 * 1024));
    }

    [Fact]
    public void WarningsCoverTruncationEncodingLinksAndMixedLineEndings()
    {
        IReadOnlyList<string> warnings = FileHeader.Warnings(TextContent(["a"], LineEnding.Mixed, 1, true, ContentNote.NotUtf8, "target.txt"));
        Assert.Equal(4, warnings.Count);
        Assert.StartsWith("Truncated", warnings[0]);
        Assert.Equal("The file is not UTF-8: shown as ISO-8859-1", warnings[1]);
        Assert.Equal("Link to target.txt", warnings[2]);
        Assert.Contains("mixed", warnings[3]);
        Assert.Empty(FileHeader.Warnings(TextContent(["a"], LineEnding.Lf, 1, false, null, null)));
    }

    [Fact]
    public void PlaceholderExplainsWhyThereIsNoText()
    {
        Assert.Null(FileHeader.Placeholder(TextContent(["a"], LineEnding.Lf, 1, false, null, null)));
        Assert.Equal("The file is empty", FileHeader.Placeholder(TextContent([], LineEnding.None, 0, false, null, null)));
        var binary = new FileContent(P, [], "", null, LineEnding.None, false, true, 2048, null, null, null);
        Assert.Equal("Binary file (2.0 KB): its content is not shown", FileHeader.Placeholder(binary));
        Assert.Equal("Binary · 2.0 KB", FileHeader.Details(binary));
        var missing = new FileContent(P, [], "", null, LineEnding.None, false, false, 0, null, ContentNote.Missing, null);
        Assert.Equal("The file no longer exists", FileHeader.Placeholder(missing));
        Assert.Equal("", FileHeader.Details(missing));
    }

    [Fact]
    public void WarningsAndPlaceholdersAreShownInSpanish()
    {
        using var _ = CultureScope.Spanish();
        IReadOnlyList<string> warnings = FileHeader.Warnings(TextContent(["a"], LineEnding.Mixed, 1, true, ContentNote.NotUtf8, "destino.txt"));
        Assert.StartsWith("Truncado", warnings[0]); // lang-ok
        Assert.Equal("El archivo no es UTF-8: se muestra como ISO-8859-1", warnings[1]); // lang-ok
        Assert.Equal("Enlace a destino.txt", warnings[2]); // lang-ok
        Assert.Contains("mezclados", warnings[3]); // lang-ok
        Assert.Equal("El archivo está vacío", FileHeader.Placeholder(TextContent([], LineEnding.None, 0, false, null, null))); // lang-ok
        var binary = new FileContent(P, [], "", null, LineEnding.None, false, true, 2048, null, null, null);
        Assert.Equal("Archivo binario (2,0 KB): no se muestra su contenido", FileHeader.Placeholder(binary)); // lang-ok
    }
}
