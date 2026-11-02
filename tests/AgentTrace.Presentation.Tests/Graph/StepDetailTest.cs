using AgentTrace.Core;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;
using static AgentTrace.Presentation.Tests.Graph.GraphTestKit;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class StepDetailTest
{
    private static FlowGraph BuildGraph()
    {
        var b = new FlowBuilder();
        List<AgentEvent> events =
        [
            new UserMessage(H(null, 0), "do this\nand that"),
            new Thinking(H(null, 1), "thinking", TimeSpan.FromSeconds(2)), new AssistantText(H(null, 1), "going"),
            new TokenUsage(H(null, 1), "m", 1, 2, 3, 4),
            new ToolCall(H(null, 2), "w", "Write", ToolKind.Write, "a.txt", "{\"file_path\":\"a.txt\"}"),
            new ToolCall(H(null, 2), "x", "Bash", ToolKind.Execute, "ls", "{}"),
            new ToolResult(H(null, 4), "w", false, new string('x', StepDetail.MaxOutput + 50)),
            new ToolResult(H(null, 5), "x", true, "boom"),
            new FileChanged(H(null, 4), new FilePath("a.txt"), ChangeKind.Created, "w"),
            new AssistantText(H(null, 6), "done"), new TurnEnded(H(null, 7), TimeSpan.FromSeconds(7)),
        ];
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }

    private static StepDetail? Detail(GraphSelection? selection, IReadOnlyList<ChangeEntry>? changes = null) =>
        StepDetail.Of(BuildGraph(), selection, TimeZoneInfo.Utc, changes);

    private static string? Field(StepDetail d, string label) => d.Fields.Where(f => f.Label == label).Select(f => f.Value).FirstOrDefault();

    [Fact]
    public void DescribesAStepWithItsStateTimesTokensAndFiles()
    {
        StepDetail d = Detail(GraphSelection.ForStep("w"))!;
        Assert.Equal("Write · a.txt", d.Title);
        Assert.Equal("write", Field(d, "Kind"));
        Assert.Equal("OK", Field(d, "State"));
        Assert.Equal("10:00:02", Field(d, "Start"));
        Assert.Equal("2.0 s", Field(d, "Duration"));
        Assert.Equal("input 1 · output 2 · cache read 3 · cache created 4 (shared by 2 calls)", Field(d, "Tokens"));
        Assert.Equal<FileTouch>([new FileTouch(new FilePath("a.txt"), ChangeKind.Created)], d.Files);
        Assert.Equal("{\"file_path\":\"a.txt\"}", d.Input);
    }

    [Fact]
    public void KeepsTheThinkingAndTheWordsBeforeTheCall()
    {
        StepDetail d = Detail(GraphSelection.ForStep("w"))!;
        Assert.Equal(2, d.Notes.Count);
        Assert.True(d.Notes[0].StartsWith("Reasoning · 1 step · 2.0 s\nthinking", StringComparison.Ordinal), d.Notes[0]);
        Assert.Equal("Said: going", d.Notes[1]);
    }

    [Fact]
    public void TruncatesLongOutputAndSaysSo()
    {
        StepDetail d = Detail(GraphSelection.ForStep("w"))!;
        Assert.True(d.Output.EndsWith("… (truncated: 50 more characters)", StringComparison.Ordinal), d.Output[^40..]);
        Assert.Equal(StepDetail.MaxOutput + "\n… (truncated: 50 more characters)".Length, d.Output.Length);
    }

    [Fact]
    public void ErrorsSaySoInWords()
    {
        StepDetail d = Detail(GraphSelection.ForStep("x"))!;
        Assert.Equal("error", Field(d, "State"));
        Assert.Equal("boom", d.Output);
        Assert.Empty(d.Files);
    }

    private static ChangeEntry Entry(string file, Confidence confidence, params string[] calls)
    {
        ValueList<ToolCall> candidates = calls.Select(id => new ToolCall(H(null, 2), id, "Bash", ToolKind.Execute, "", "{}")).ToValueList();
        return new ChangeEntry(new FileChange(new FilePath(file), ChangeKind.Modified, null), new Attribution(confidence, candidates));
    }

    [Fact]
    public void AddsTheFilesGitAttributesToASingleCallAndMarksThem()
    {
        List<ChangeEntry> changes =
        [
            Entry("g.txt", Confidence.Probable, "x"),
            Entry("a.txt", Confidence.Exact, "w"),
            Entry("amb.txt", Confidence.Ambiguous, "x", "w"),
            Entry("other.txt", Confidence.Exact, "w"),
            Entry("ext.txt", Confidence.None),
        ];
        StepDetail bash = Detail(GraphSelection.ForStep("x"), changes)!;
        Assert.Equal<FileTouch>([new FileTouch(new FilePath("g.txt"), ChangeKind.Modified)], bash.Files);
        Assert.Equal<FilePath>([new FilePath("g.txt")], bash.ViaGit);
        StepDetail write = Detail(GraphSelection.ForStep("w"), changes)!;
        Assert.True(write.ViaGit.SequenceEqual([new FilePath("other.txt")]), "a.txt is already declared by the trace and the ambiguous one is not assigned");
        Assert.Equal<FileTouch>([new FileTouch(new FilePath("a.txt"), ChangeKind.Created), new FileTouch(new FilePath("other.txt"), ChangeKind.Modified)],
            write.Files);
    }

    [Fact]
    public void DescribesATurn()
    {
        StepDetail d = Detail(GraphSelection.ForTurn(0))!;
        Assert.Equal("Turn 1", d.Title);
        Assert.Equal("finished", Field(d, "State"));
        Assert.Equal("2 (1 error)", Field(d, "Steps"));
        Assert.Equal("7.0 s", Field(d, "Duration"));
        Assert.Equal("do this\nand that", d.Input);
        Assert.Equal("done", d.Output);
        Assert.Equal("Prompt", d.InputLabel);
    }

    [Fact]
    public void DescribesAStepAndATurnInSpanish()
    {
        using var _ = CultureScope.Spanish();
        StepDetail step = Detail(GraphSelection.ForStep("w"))!;
        Assert.Equal("escritura", Field(step, "Tipo")); // lang-ok
        Assert.Equal("entrada 1 · salida 2 · caché leída 3 · caché creada 4 (compartidos por 2 llamadas)", Field(step, "Tokens")); // lang-ok
        Assert.StartsWith("Razonamiento · 1 paso · 2,0 s\n", step.Notes[0], StringComparison.Ordinal); // lang-ok
        StepDetail turn = Detail(GraphSelection.ForTurn(0))!;
        Assert.Equal("Turno 1", turn.Title); // lang-ok
        Assert.Equal("2 (1 error)", Field(turn, "Pasos")); // lang-ok
        Assert.Equal("Petición", turn.InputLabel); // lang-ok
    }

    [Fact]
    public void UnknownOrMissingSelectionsGiveNothing()
    {
        Assert.Null(Detail(null));
        Assert.Null(Detail(GraphSelection.ForStep("nope")));
        Assert.Null(Detail(GraphSelection.ForTurn(9)));
    }

    [Fact]
    public void TheDetailIsAValueSoAnUnchangedStepDoesNotRebuildThePanel()
    {
        Assert.Equal(Detail(GraphSelection.ForStep("w")), Detail(GraphSelection.ForStep("w")));
        Assert.False(Detail(GraphSelection.ForStep("w"))!.Equals(Detail(GraphSelection.ForStep("x"))));
    }
}
