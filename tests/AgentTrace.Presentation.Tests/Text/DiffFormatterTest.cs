using AgentTrace.Core;
using AgentTrace.Presentation.Text;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class DiffFormatterTest
{
    private static readonly FilePath P = new("a.txt");

    private static string Format(string? before, string? after) => DiffFormatter.Format(new FileVersions(P, before, after, null), "a.txt");

    [Fact]
    public void ModifiedFileShowsHeadersCountsAndHunks()
    {
        string text = Format("one\ntwo\nthree\n", "one\nTWO\nthree\n");
        Assert.True(text.StartsWith("--- a/a.txt\n+++ b/a.txt\n", StringComparison.Ordinal), text);
        Assert.Contains("(+1 −1)", text);
        Assert.Contains("@@ -1,3 +1,3 @@", text);
        Assert.Contains("\n one\n-two\n+TWO\n three\n", text);
    }

    [Fact]
    public void RowsCarryTheKindAndBothLineNumbers()
    {
        IReadOnlyList<DiffRow> rows = DiffFormatter.Rows(new FileVersions(P, "one\ntwo\nthree\n", "one\nTWO\nthree\n", null), "a.txt");
        Assert.Equal(
            [DiffRow.RowKind.Header, DiffRow.RowKind.Header, DiffRow.RowKind.Header, DiffRow.RowKind.Hunk, DiffRow.RowKind.Context,
                DiffRow.RowKind.Del, DiffRow.RowKind.Add, DiffRow.RowKind.Context],
            rows.Select(r => r.Kind).ToList());
        DiffRow context = rows[4];
        Assert.Equal(1, context.OldLine);
        Assert.Equal(1, context.NewLine);
        DiffRow deleted = rows[5];
        Assert.Equal("two", deleted.Text);
        Assert.Equal(2, deleted.OldLine);
        Assert.Null(deleted.NewLine);
        DiffRow added = rows[6];
        Assert.Equal("TWO", added.Text);
        Assert.Null(added.OldLine);
        Assert.Equal(2, added.NewLine);
    }

    [Fact]
    public void DisplayTextKeepsTheClassicPrefixesSoTheDiffDoesNotDependOnColour()
    {
        Assert.Equal(" x", new DiffRow(DiffRow.RowKind.Context, 1, 1, "x").DisplayText);
        Assert.Equal("+x", new DiffRow(DiffRow.RowKind.Add, null, 1, "x").DisplayText);
        Assert.Equal("-x", new DiffRow(DiffRow.RowKind.Del, 1, null, "x").DisplayText);
        Assert.Equal("@@ -1,1 +1,1 @@", new DiffRow(DiffRow.RowKind.Hunk, null, null, "@@ -1,1 +1,1 @@").DisplayText);
        Assert.True(new DiffRow(DiffRow.RowKind.Add, null, 1, "x").IsCode);
        Assert.False(DiffRow.Header("---").IsCode);
    }

    [Fact]
    public void AMessageIsASingleRowAndTheTextHasNoTrailingNewline()
    {
        IReadOnlyList<DiffRow> rows = DiffFormatter.Rows(FileVersions.Unavailable(P, UnavailableReason.Binary), "a.txt");
        Assert.Single(rows);
        Assert.Equal(DiffRow.RowKind.Message, rows[0].Kind);
        Assert.Equal("Cannot show the content: binary", DiffFormatter.Text(rows));
    }

    [Fact]
    public void AnEmptySideOfAHunkIsNumberedWithThePreviousLine()
    {
        Assert.Contains("@@ -0,0 +1,1 @@", Format(null, "hello\n"));
        Assert.Contains("@@ -1,1 +0,0 @@", Format("bye\n", null));
    }

    [Fact]
    public void NewAndDeletedFilesUseDevNull()
    {
        Assert.StartsWith("--- /dev/null\n+++ b/a.txt\n", Format(null, "hello\n"), StringComparison.Ordinal);
        Assert.StartsWith("--- a/a.txt\n+++ /dev/null\n", Format("bye\n", null), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainsWhenThereIsNothingToShow()
    {
        Assert.Contains("line endings", Format("a\r\nb\r\n", "a\nb\n"));
        Assert.Equal("New empty file", Format(null, ""));
        Assert.Equal("File deleted (it was empty)", Format("", null));
    }

    [Fact]
    public void UnavailableVersionsAreExplained()
    {
        Assert.Equal("Cannot show the content: binary",
            DiffFormatter.Format(FileVersions.Unavailable(P, UnavailableReason.Binary), "a.txt"));
        string big = string.Concat(Enumerable.Repeat("x\n", 25_000));
        Assert.StartsWith("Cannot show the diff: too large", Format(big, big + "y\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void MessagesAreShownInSpanish()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("No se puede mostrar el contenido: binario", // lang-ok
            DiffFormatter.Format(FileVersions.Unavailable(P, UnavailableReason.Binary), "a.txt"));
        Assert.Equal("Archivo nuevo vacío", Format(null, "")); // lang-ok
        Assert.Equal("Archivo borrado (estaba vacío)", Format("", null)); // lang-ok
        Assert.Contains("finales de línea", Format("a\r\nb\r\n", "a\nb\n")); // lang-ok
    }
}
