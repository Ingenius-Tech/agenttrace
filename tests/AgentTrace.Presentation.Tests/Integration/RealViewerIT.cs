using AgentTrace.Core;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Text;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Integration;

/// <summary>
/// Acceptance of the viewer with REAL files created or modified by Claude Code in this project (real git and real trace).
/// It is enabled with <c>AGENTTRACE_ACCEPTANCE_VIEWER=&lt;file&gt;=&lt;LF|CRLF|MODIFIED&gt;[,...]</c>: LF and CRLF are new files with
/// a non-ASCII letter and that line ending; MODIFIED is a versioned file where some line was changed. Without the variable, it is skipped.
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class RealViewerIT
{
    [Fact]
    public void RealFilesAreShownAndTheirDiffsHaveTheRightRows()
    {
        (RealAcceptance a, string spec) = RealAcceptance.StartOrSkip("AGENTTRACE_ACCEPTANCE_VIEWER");
        using (a)
        {
            foreach (string item in spec.Split(','))
            {
                string[] parts = item.Trim().Split('=');
                FilePath file = a.Project.Combine(parts[0]);
                string expected = parts[1];
                Eventually.Until(parts[0] + " is not in the change list", () => a.Find(file) is not null, 25_000, 50);

                // What the File tab shows
                FileContent content = FileContentReader.Read(file);
                Assert.True(content.Readable, parts[0] + ": " + content.Problem);
                Assert.Equal("UTF-8", content.Encoding);
                Assert.NotEmpty(content.Lines);

                // What the Inspector shows
                IReadOnlyList<DiffRow> rows = DiffFormatter.Rows(a.Observer.Versions(file), parts[0]);
                long added = rows.Count(r => r.Kind == DiffRow.RowKind.Add);
                long deleted = rows.Count(r => r.Kind == DiffRow.RowKind.Del);
                Console.WriteLine("[REAL-VIEWER] " + parts[0] + " → " + content.Encoding + ", " + content.LineEnding + ", "
                    + content.Lines.Count + " lines; diff: +" + added + " −" + deleted + " (" + rows.Count + " rows)");

                switch (expected)
                {
                    case "LF" or "CRLF":
                        Assert.Equal(Enum.Parse<LineEnding>(expected, true), content.LineEnding); // line ending of the file
                        Assert.Contains(content.Lines, l => l.Contains('ñ')); // the ñ must be read correctly // lang-ok
                        Assert.Equal(content.Lines.Count, added); // a new file is all added lines
                        Assert.Equal(0, deleted);
                        Assert.All(rows.Where(r => r.Kind == DiffRow.RowKind.Add), r => Assert.True(r.OldLine is null && r.NewLine is not null));
                        Assert.Contains(rows, r => r.Kind == DiffRow.RowKind.Add && r.Text.Contains('ñ')); // the ñ must show in the diff // lang-ok
                        break;
                    case "MODIFIED":
                        Assert.True(added > 0 && deleted > 0, "a changed line must give one added and one deleted");
                        Assert.Contains(rows, r => r.Kind == DiffRow.RowKind.Context && r.OldLine is not null && r.NewLine is not null);
                        Assert.Contains(rows, r => r.Kind == DiffRow.RowKind.Hunk);
                        break;
                    default:
                        Assert.Fail("Unknown type: " + expected);
                        break;
                }
            }
        }
    }
}
