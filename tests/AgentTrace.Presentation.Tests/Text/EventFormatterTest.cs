using System.Text.RegularExpressions;
using AgentTrace.Core;
using AgentTrace.Presentation.Text;
using static AgentTrace.Core.AgentEvent;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Text;

public sealed class EventFormatterTest
{
    private static EventHeader Header(string? subagent, DateTimeOffset? ts) => new(new SessionId("s"), "test", subagent, ts, new SourceRef(new FilePath("t"), 1));

    private static readonly EventHeader H = Header(null, null);

    [Fact]
    public void IgnoredLinesAreNotShown()
    {
        Assert.Null(EventFormatter.Line(new Skipped(H, "mode", SkipReason.Ignored)));
    }

    [Fact]
    public void ShowsToolCallsWithNameAndInputOnOneLine()
    {
        string line = EventFormatter.Line(new ToolCall(H, "1", "Bash", "{\"command\":\"ls\n -la\"}"))!;
        Assert.True(line.EndsWith("[Tool] Bash · {\"command\":\"ls -la\"}", StringComparison.Ordinal), line);
        Assert.True(line.StartsWith("--:--:--", StringComparison.Ordinal), line);
    }

    [Fact]
    public void TruncatesLongTextAndMarksSubagentsAndExternalChanges()
    {
        string line = EventFormatter.Line(new AssistantText(Header("abc", null), new string('x', 500)))!;
        Assert.Contains("↳ abc · Assistant · ", line);
        Assert.True(line.EndsWith('…'), line);
        Assert.True(line.Length < 260, "length " + line.Length);
        // the path of a FilePath is always absolute (in Java, Path.of("a.txt") was relative)
        string external = EventFormatter.Line(new FileChanged(H, new FilePath("a.txt"), ChangeKind.Modified, null))!;
        Assert.True(external.EndsWith("modified " + new FilePath("a.txt") + " (external)", StringComparison.Ordinal), external);
    }

    [Fact]
    public void LabelsEveryKindOfChange()
    {
        Assert.Equal("created", EventFormatter.KindLabel(ChangeKind.Created));
        Assert.Equal("modified", EventFormatter.KindLabel(ChangeKind.Modified));
        Assert.Equal("deleted", EventFormatter.KindLabel(ChangeKind.Deleted));
        Assert.EndsWith("deleted " + new FilePath("x"), EventFormatter.Line(new FileChanged(H, new FilePath("x"), ChangeKind.Deleted, "1")), StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsReadsWithTheirPath()
    {
        FilePath path = new FilePath("src").Combine("A.java");
        Assert.EndsWith("[Read] " + path, EventFormatter.Line(new FileRead(H, path, "1")), StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsASubagentLaunchWithItsDescription()
    {
        Assert.EndsWith("↳ Subagent investigate the module", EventFormatter.Line(new SubagentStarted(H, "c1", "abc", "investigate the module")), StringComparison.Ordinal);
        Assert.Null(EventFormatter.PathOf(new SubagentStarted(H, "c1", "abc", "x")));
    }

    [Fact]
    public void OnlyChangesAndReadsPointToAFile()
    {
        Assert.Equal(new FilePath("a.txt"), EventFormatter.PathOf(new FileChanged(H, new FilePath("a.txt"), ChangeKind.Modified, "1")));
        Assert.Equal(new FilePath("b.txt"), EventFormatter.PathOf(new FileRead(H, new FilePath("b.txt"), "2")));
        Assert.Null(EventFormatter.PathOf(new ToolCall(H, "3", "Bash", "{\"command\":\"cat x.txt\"}")));
        Assert.Null(EventFormatter.PathOf(new UserMessage(H, "look at src/A.java")));
    }

    [Fact]
    public void ShowsErrorsAndDurations()
    {
        Assert.EndsWith("[Error] boom", EventFormatter.Line(new ToolResult(H, "1", true, "boom")), StringComparison.Ordinal);
        // Java gave "22.4 s" (default locale); here the decimal separator is the one of the current culture
        Assert.EndsWith("Turn ended · 22.4 s", EventFormatter.Line(new TurnEnded(H, TimeSpan.FromMilliseconds(22_376))), StringComparison.Ordinal);
        Assert.EndsWith("Thinking", EventFormatter.Line(new Thinking(H, "", null)), StringComparison.Ordinal);
    }

    [Fact]
    public void UsesTheTimestampWhenPresent()
    {
        string line = EventFormatter.Line(new UserMessage(Header(null, DateTimeOffset.Parse("2026-01-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)), "hello"))!;
        Assert.Matches(new Regex("^\\d\\d:\\d\\d:\\d\\d {2}User · hello$"), line);
    }

    /// <summary>Added case: seconds use the decimal separator of the current culture (comma in Spanish).</summary>
    [Fact]
    public void SecondsUseTheCommaOfTheSpanishCulture()
    {
        using var _ = CultureScope.Spanish();
        Assert.EndsWith("Turno terminado · 22,4 s", EventFormatter.Line(new TurnEnded(H, TimeSpan.FromMilliseconds(22_376))), StringComparison.Ordinal); // lang-ok
    }

    [Fact]
    public void LinesAreShownInSpanish()
    {
        using var _ = CultureScope.Spanish();
        Assert.EndsWith("[Herramienta] Bash · {}", EventFormatter.Line(new ToolCall(H, "1", "Bash", "{}")), StringComparison.Ordinal); // lang-ok
        Assert.EndsWith("↳ Subagente investigar el módulo", EventFormatter.Line(new SubagentStarted(H, "c1", "abc", "investigar el módulo")), StringComparison.Ordinal); // lang-ok
        Assert.EndsWith("modificado " + new FilePath("a.txt") + " (externo)", // lang-ok
            EventFormatter.Line(new FileChanged(H, new FilePath("a.txt"), ChangeKind.Modified, null)), StringComparison.Ordinal);
        Assert.EndsWith("Razonando", EventFormatter.Line(new Thinking(H, "", null)), StringComparison.Ordinal); // lang-ok
    }
}
