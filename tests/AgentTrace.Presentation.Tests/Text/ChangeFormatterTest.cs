using AgentTrace.Core;
using AgentTrace.Presentation.Text;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class ChangeFormatterTest
{
    private static readonly FilePath Project = new("project");
    private static readonly EventHeader H = new(new SessionId("s"), "t", null, null, new SourceRef(new FilePath("x"), 1));

    private static ToolCall Call(string tool, string input) => new(H, "id", tool, input);

    private static ChangeEntry Entry(ChangeKind kind, string file, Attribution attribution) =>
        new(new FileChange(Project.Combine(file), kind, DateTimeOffset.UnixEpoch), attribution);

    [Fact]
    public void RowShowsSymbolRelativePathAndAttribution()
    {
        Assert.Equal("~  src/a.txt    ≈ Bash · {\"command\":\"ls\"}",
            ChangeFormatter.Row(Project, Entry(ChangeKind.Modified, "src/a.txt",
                new Attribution(Confidence.Probable, [Call("Bash", "{\"command\":\"ls\"}")]))));
        Assert.StartsWith("+  n.txt", ChangeFormatter.Row(Project, Entry(ChangeKind.Created, "n.txt", Attribution.None())), StringComparison.Ordinal);
        Assert.StartsWith("−  b.txt", ChangeFormatter.Row(Project, Entry(ChangeKind.Deleted, "b.txt", Attribution.Undated())), StringComparison.Ordinal);
    }

    [Fact]
    public void DescribesEveryConfidenceLevel()
    {
        Assert.Equal("← Write · {}", ChangeFormatter.AttributionText(new Attribution(Confidence.Exact, [Call("Write", "{}")])));
        Assert.Equal("? 3 candidates: Bash, Edit", ChangeFormatter.AttributionText(new Attribution(Confidence.Ambiguous,
            [Call("Bash", "{}"), Call("Bash", "{}"), Call("Edit", "{}")])));
        Assert.Equal("external", ChangeFormatter.AttributionText(Attribution.None()));
        Assert.Equal("unknown time", ChangeFormatter.AttributionText(Attribution.Undated()));
    }

    [Fact]
    public void DescribesEveryConfidenceLevelInSpanish()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("? 3 posibles: Bash, Edit", ChangeFormatter.AttributionText(new Attribution(Confidence.Ambiguous, // lang-ok
            [Call("Bash", "{}"), Call("Bash", "{}"), Call("Edit", "{}")])));
        Assert.Equal("externo", ChangeFormatter.AttributionText(Attribution.None())); // lang-ok
        Assert.Equal("hora desconocida", ChangeFormatter.AttributionText(Attribution.Undated())); // lang-ok
    }

    [Fact]
    public void LongInputsAreFlattenedAndTruncated()
    {
        string text = ChangeFormatter.AttributionText(new Attribution(Confidence.Exact,
            [Call("Bash", "line one\n   line two " + new string('x', 200))]));
        Assert.True(text.StartsWith("← Bash · line one line two ", StringComparison.Ordinal), text);
        Assert.True(text.EndsWith('…'), text);
        Assert.True(text.Length < 90, "length " + text.Length);
    }

    [Fact]
    public void PathsOutsideTheProjectStayAbsolute()
    {
        FilePath outside = Project.Parent!.Value.Combine("other").Combine("f.txt");
        Assert.Equal(outside.ToString().Replace('\\', '/'), ChangeFormatter.Relative(Project, outside));
    }

    /// <summary>Added case: Java's <c>Path.relativize</c> gives an empty string for the root itself, not ".".</summary>
    [Fact]
    public void TheProjectRootItselfIsEmpty()
    {
        Assert.Equal("", ChangeFormatter.Relative(Project, Project));
    }
}
