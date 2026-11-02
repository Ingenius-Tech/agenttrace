using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode.Tests;

public sealed class ClaudeCodeLineParserTest
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    /// <summary>The full synthetic trace, parsed once (the <c>@BeforeAll</c> of the Java version).</summary>
    private static readonly List<List<AgentEvent>> PerLine = [];
    private static readonly List<AgentEvent> All = [];

    static ClaudeCodeLineParserTest()
    {
        FilePath trace = Resource("claude/projects/D--fake-proj/" + Session + ".jsonl");
        var parser = new ClaudeCodeLineParser(trace);
        long n = 0;
        foreach (string line in File.ReadAllLines(trace.Value))
        {
            List<AgentEvent> events = [.. parser.Parse(line, ++n)];
            PerLine.Add(events);
            All.AddRange(events);
        }
    }

    private static FilePath Resource(string name) => TestSupport.Resource(name);

    private static List<T> Of<T>() where T : AgentEvent => All.OfType<T>().ToList();

    private static FilePath P(string path) => new(path);

    [Fact]
    public void EveryLineProducesAtLeastOneEvent()
    {
        Assert.Equal(39, PerLine.Count);
        Assert.True(PerLine.All(l => l.Count != 0));
    }

    [Fact]
    public void UserMessageAndMetaLine()
    {
        Assert.Equal(AgentEvent.SkipReason.Ignored, Assert.IsType<AgentEvent.Skipped>(PerLine[1][0]).Reason);
        var u = Assert.IsType<AgentEvent.UserMessage>(PerLine[2][0]);
        Assert.Equal("hola agente", u.Text); // lang-ok: fixture text
        Assert.Equal(Session, u.Header.SessionId.Value);
        Assert.Equal("claude-code", u.Header.Agent);
        Assert.Null(u.Header.SubagentId);
        Assert.Equal(3, u.Header.Source.LineNumber);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 10, 0, 1, TimeSpan.Zero), u.Header.Timestamp);
        Assert.Equal(TimeSpan.Zero, u.Header.Timestamp!.Value.Offset);
    }

    [Fact]
    public void MessageSplitInSeveralLinesCountsTokensOnce()
    {
        List<AgentEvent.TokenUsage> usages = Of<AgentEvent.TokenUsage>();
        // msg_1 (3 lines), msg_2, msg_3, msg_4 and msg_5: five messages, five usages
        Assert.Equal(5, usages.Count);
        AgentEvent.TokenUsage first = usages[0];
        Assert.Equal(10, first.Input);
        Assert.Equal(50, first.Output);
        Assert.Equal(100, first.CacheRead);
        Assert.Equal(20, first.CacheCreation);
        Assert.Equal(50 + 30 + 20 + 10 + 8, usages.Sum(u => u.Output));
    }

    [Fact]
    public void ThinkingCarriesItsDuration()
    {
        var t = Assert.IsType<AgentEvent.Thinking>(PerLine[3][0]);
        Assert.Equal("", t.Text);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), t.Duration);
    }

    [Fact]
    public void ToolCallAndResultAreLinkedByCallId()
    {
        var call = Assert.IsType<AgentEvent.ToolCall>(PerLine[5][0]);
        Assert.Equal("Bash", call.ToolName);
        Assert.Equal("{\"command\":\"ls\"}", call.InputJson);
        var result = Assert.IsType<AgentEvent.ToolResult>(PerLine[6][0]);
        Assert.Equal(call.CallId, result.CallId);
        Assert.False(result.Error);
        Assert.Equal("a.txt", result.Output);
        Assert.Single(PerLine[6]);
    }

    [Fact]
    public void ErrorResultWithArrayContent()
    {
        var r = Assert.IsType<AgentEvent.ToolResult>(PerLine[12][0]);
        Assert.True(r.Error);
        Assert.Equal("boom", r.Output);
        Assert.Single(PerLine[12]);
    }

    [Fact]
    public void WriteAndEditProduceFileChangesLinkedToTheCall()
    {
        var created = Assert.IsType<AgentEvent.FileChanged>(PerLine[8][1]);
        Assert.Equal(AgentEvent.ChangeKind.Created, created.Kind);
        Assert.Equal("toolu_2", created.CallId);
        Assert.Equal(P("C:\\p\\a.txt"), created.Path);
        var modified = Assert.IsType<AgentEvent.FileChanged>(PerLine[10][1]);
        Assert.Equal(AgentEvent.ChangeKind.Modified, modified.Kind);
        Assert.Equal("toolu_3", modified.CallId);
    }

    /// <summary>A write call and its result, parsed with a dedicated parser.</summary>
    private static AgentEvent.ChangeKind Classify(string tool, string input, string toolUseResult)
    {
        var parser = new ClaudeCodeLineParser(Resource("claude/projects/D--fake-proj/" + Session + ".jsonl"));
        string call = "{\"type\":\"assistant\",\"sessionId\":\"" + Session + "\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"" + tool + "\",\"input\":" + input + "}]}}";
        string result = "{\"type\":\"user\",\"sessionId\":\"" + Session + "\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"ok\"}]},\"toolUseResult\":" + toolUseResult + "}";
        _ = parser.Parse(call, 1).ToList();
        return parser.Parse(result, 2).OfType<AgentEvent.FileChanged>().Single().Kind;
    }

    [Theory]
    [InlineData("Write", "{}", "{\"type\":\"create\",\"filePath\":\"/p/a\",\"originalFile\":null}", AgentEvent.ChangeKind.Created)]
    [InlineData("Write", "{}", "{\"type\":\"update\",\"filePath\":\"/p/a\",\"originalFile\":\"x\"}", AgentEvent.ChangeKind.Modified)]
    [InlineData("Write", "{}", "{\"type\":\"update\",\"filePath\":\"/p/a\",\"originalFile\":null}", AgentEvent.ChangeKind.Modified)]
    [InlineData("Write", "{}", "{\"filePath\":\"/p/a\",\"originalFile\":null}", AgentEvent.ChangeKind.Created)]
    [InlineData("Write", "{}", "{\"filePath\":\"/p/a\",\"originalFile\":\"x\"}", AgentEvent.ChangeKind.Modified)]
    [InlineData("Edit", "{\"old_string\":\"a\"}", "{\"filePath\":\"/p/a\",\"originalFile\":null}", AgentEvent.ChangeKind.Modified)]
    [InlineData("Edit", "{\"old_string\":\"a\"}", "{\"filePath\":\"/p/a\",\"originalFile\":\"a\"}", AgentEvent.ChangeKind.Modified)]
    [InlineData("Edit", "{\"old_string\":\"\"}", "{\"filePath\":\"/p/a\",\"originalFile\":null}", AgentEvent.ChangeKind.Created)]
    [InlineData("MultiEdit", "{\"edits\":[{\"old_string\":\"\"}]}", "{\"filePath\":\"/p/a\"}", AgentEvent.ChangeKind.Created)]
    [InlineData("MultiEdit", "{\"edits\":[{\"old_string\":\"a\"}]}", "{\"filePath\":\"/p/a\"}", AgentEvent.ChangeKind.Modified)]
    public void WriteClassificationFollowsTheRule(string tool, string input, string toolUseResult, AgentEvent.ChangeKind expected)
        => Assert.Equal(expected, Classify(tool, input, toolUseResult));

    [Fact]
    public void EditedTextFileAttachmentIsAnExternalChange()
    {
        var f = Assert.IsType<AgentEvent.FileChanged>(PerLine[13][0]);
        Assert.Null(f.CallId);
        Assert.Equal(P("C:\\p\\b.txt"), f.Path);
        Assert.Equal(AgentEvent.SkipReason.Ignored, Assert.IsType<AgentEvent.Skipped>(PerLine[14][0]).Reason);
    }

    [Fact]
    public void TurnDuration()
    {
        var t = Assert.IsType<AgentEvent.TurnEnded>(PerLine[15][0]);
        Assert.Equal(TimeSpan.FromSeconds(4), t.Duration);
        Assert.Equal(AgentEvent.SkipReason.Ignored, Assert.IsType<AgentEvent.Skipped>(PerLine[16][0]).Reason);
    }

    [Fact]
    public void IgnoredUnknownAndCorruptLines()
    {
        Assert.Equal(AgentEvent.SkipReason.Ignored, Assert.IsType<AgentEvent.Skipped>(PerLine[0][0]).Reason);
        var unknown = Assert.IsType<AgentEvent.Skipped>(PerLine[17][0]);
        Assert.Equal(AgentEvent.SkipReason.Unknown, unknown.Reason);
        Assert.Equal("future-thing", unknown.RawType);
        Assert.Equal(AgentEvent.SkipReason.Malformed, Assert.IsType<AgentEvent.Skipped>(PerLine[18][0]).Reason);
    }

    [Fact]
    public void RequestBlobCopiesAreNotCountedAsMessages()
    {
        var blob = Assert.IsType<AgentEvent.Skipped>(PerLine[19][0]);
        Assert.Equal(AgentEvent.SkipReason.Ignored, blob.Reason);
        Assert.True(Of<AgentEvent.AssistantText>().All(t => !t.Text.Contains("copia de peticion", StringComparison.Ordinal))); // lang-ok: fixture text
    }

    [Fact]
    public void SubagentFileGetsItsIdAndParentSession()
    {
        FilePath sub = Resource("claude/projects/D--fake-proj/" + Session + "/subagents/agent-abc123.jsonl");
        var parser = new ClaudeCodeLineParser(sub);
        long n = 0;
        var events = new List<AgentEvent>();
        foreach (string line in File.ReadAllLines(sub.Value)) events.AddRange(parser.Parse(line, ++n));
        Assert.True(events.All(e => "abc123" == e.Header.SubagentId));
        Assert.True(events.All(e => Session == e.Header.SessionId.Value));
        Assert.Equal(1, events.Count(e => e is AgentEvent.TokenUsage));
    }

    [Fact]
    public void ASuccessfulReadProducesAFileReadWithThePathOfTheCallEvenForImages()
    {
        // text: the result carries file.filePath; image: it does not, only the call input has the path
        Assert.Equal(2, PerLine[22].Count);
        var text = Assert.IsType<AgentEvent.FileRead>(PerLine[22][1]);
        Assert.Equal(P("C:\\p\\leido.txt"), text.Path); // lang-ok: fixture name
        Assert.Equal("toolu_6", text.CallId);
        Assert.IsType<AgentEvent.ToolResult>(PerLine[22][0]);
        var image = Assert.IsType<AgentEvent.FileRead>(PerLine[24][1]);
        Assert.Equal(P("C:\\p\\captura.png"), image.Path); // lang-ok: fixture name
        Assert.Equal(2, Of<AgentEvent.FileRead>().Count);
    }

    [Fact]
    public void AFailedReadIsAnErrorResultNotARead()
    {
        Assert.Single(PerLine[26]);
        Assert.True(Assert.IsType<AgentEvent.ToolResult>(PerLine[26][0]).Error);
    }

    private static FilePath? TargetOf(string tool, string input)
    {
        var parser = new ClaudeCodeLineParser(Resource("claude/projects/D--fake-proj/" + Session + ".jsonl"));
        string call = "{\"type\":\"assistant\",\"sessionId\":\"" + Session + "\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"" + tool + "\",\"input\":" + input + "}]}}";
        return parser.Parse(call, 1).OfType<AgentEvent.ToolCall>().Single().Target;
    }

    [Theory]
    [InlineData("Read", "{\"file_path\":\"/p/a\"}", "/p/a")]
    [InlineData("Write", "{\"file_path\":\"/p/a\",\"content\":\"x\"}", "/p/a")]
    [InlineData("Edit", "{\"file_path\":\"/p/a\"}", "/p/a")]
    [InlineData("NotebookEdit", "{\"notebook_path\":\"/p/n.ipynb\"}", "/p/n.ipynb")]
    [InlineData("Read", "{\"file_path\":\"\"}", null)]
    [InlineData("Read", "{}", null)]
    [InlineData("Bash", "{\"file_path\":\"/p/a\",\"command\":\"ls\"}", null)]
    [InlineData("Grep", "{\"file_path\":\"/p/a\",\"pattern\":\"x\"}", null)]
    public void ToolCallTargetIsTheFileOfReadAndWriteTools(string tool, string input, string? expected)
        => Assert.Equal(expected is null ? null : P(expected), TargetOf(tool, input));

    [Fact]
    public void EveryToolCallCarriesItsKindAndASummary()
    {
        List<AgentEvent.ToolCall> calls = Of<AgentEvent.ToolCall>();
        // in C# the class cannot be null (it is an enum): check that it is a defined value
        Assert.True(calls.All(c => Enum.IsDefined(c.Kind)));
        AgentEvent.ToolCall bash = calls.First(c => c.CallId == "toolu_1");
        Assert.Equal(AgentEvent.ToolKind.Execute, bash.Kind);
        Assert.Equal("ls", bash.Summary);
        AgentEvent.ToolCall write = calls.First(c => c.CallId == "toolu_2");
        Assert.Equal(AgentEvent.ToolKind.Write, write.Kind);
        Assert.Equal("C:\\p\\a.txt", write.Summary);
        AgentEvent.ToolCall agent = calls.First(c => c.CallId == "toolu_5");
        Assert.Equal(AgentEvent.ToolKind.Delegate, agent.Kind);
        Assert.Equal("investigar", agent.Summary); // lang-ok: fixture text
        AgentEvent.ToolCall grep = calls.First(c => c.CallId == "toolu_9a");
        Assert.Equal(AgentEvent.ToolKind.Search, grep.Kind);
        Assert.Equal("TODO", grep.Summary);
        Assert.Equal("git status", calls.First(c => c.CallId == "toolu_9b").Summary);
        Assert.Equal(AgentEvent.ToolKind.Execute, calls.First(c => c.CallId == "toolu_9c").Kind);
    }

    [Fact]
    public void TheResultOfADelegationLinksTheCallToItsSubagent()
    {
        // line 28 of the resource: asynchronous result of toolu_5 with agentId
        List<AgentEvent> events = PerLine[27];
        Assert.Equal(2, events.Count);
        Assert.IsType<AgentEvent.ToolResult>(events[0]);
        var started = Assert.IsType<AgentEvent.SubagentStarted>(events[1]);
        Assert.Equal("toolu_5", started.ParentCallId);
        Assert.Equal("abc123", started.SubagentId);
        Assert.Equal("investigar", started.Description); // lang-ok: fixture text
    }

    [Fact]
    public void ParallelCallsOfTheSameMessageAreSeparateConsecutiveCalls()
    {
        // lines 29-31: three calls of the same message, with no results between them
        Assert.Equal(typeof(AgentEvent.ToolCall), PerLine[28][0].GetType());
        Assert.Equal(typeof(AgentEvent.ToolCall), PerLine[29][0].GetType());
        Assert.Equal(typeof(AgentEvent.ToolCall), PerLine[30][0].GetType());
        Assert.Equal(3, PerLine.GetRange(28, 3).SelectMany(l => l).Count(e => e is AgentEvent.ToolCall));
        Assert.Equal(3, PerLine.GetRange(31, 3).SelectMany(l => l).Count(e => e is AgentEvent.ToolResult));
    }

    [Fact]
    public void APeerNoticeIsIgnoredButWhatFollowsItIsStillTraced()
    {
        Assert.Equal(AgentEvent.SkipReason.Ignored, Assert.IsType<AgentEvent.Skipped>(PerLine[34][0]).Reason);
        Assert.IsType<AgentEvent.ToolCall>(PerLine[35][0]);
    }

    [Fact]
    public void AReSentRequestProducesTwoUserMessagesWithoutAnyTurnEndBetween()
    {
        Assert.Equal("reenvio mi peticion", Assert.IsType<AgentEvent.UserMessage>(PerLine[37][0]).Text); // lang-ok: fixture text
        Assert.Equal("reenvio mi peticion otra vez", Assert.IsType<AgentEvent.UserMessage>(PerLine[38][0]).Text); // lang-ok: fixture text
    }

    [Fact]
    public void TheSubagentFileLinksItselfToTheCallThatLaunchedItThroughItsMetaFile()
    {
        FilePath sub = Resource("claude/projects/D--fake-proj/" + Session + "/subagents/agent-abc123.jsonl");
        var parser = new ClaudeCodeLineParser(sub);
        string[] lines = File.ReadAllLines(sub.Value);
        IReadOnlyList<AgentEvent> first = parser.Parse(lines[0], 1);
        AgentEvent.SubagentStarted started = first.OfType<AgentEvent.SubagentStarted>().First();
        Assert.Equal("toolu_5", started.ParentCallId);
        Assert.Equal("abc123", started.SubagentId);
        Assert.Equal("investigar", started.Description); // lang-ok: fixture text
        Assert.Equal("abc123", started.Header.SubagentId);
        IReadOnlyList<AgentEvent> second = parser.Parse(lines[1], 2);
        Assert.True(second.All(e => e is not AgentEvent.SubagentStarted), "the link is emitted only once");
    }

    [Fact]
    public void ASubagentWithoutMetaFileJustDoesNotLink()
    {
        FilePath orphan = P(Path.Combine("no-meta", Session, "subagents", "agent-zzz.jsonl"));
        IReadOnlyList<AgentEvent> events = new ClaudeCodeLineParser(orphan)
            .Parse("""{"type":"user","message":{"role":"user","content":"hola"}}""", 1);
        Assert.True(events.All(e => e is not AgentEvent.SubagentStarted));
        Assert.IsType<AgentEvent.UserMessage>(events[0]);
    }

    [Fact]
    public void AReadIsNotAFileChange()
    {
        Assert.True(Of<AgentEvent.FileChanged>().All(c => !c.Path.Value.Contains("leido", StringComparison.Ordinal))); // lang-ok: fixture name
    }

    [Fact]
    public void ImageBlocksInResultsAreSummarisedInsteadOfDumpedAsBase64()
    {
        var parser = new ClaudeCodeLineParser(P(Path.Combine("x", Session + ".jsonl")));
        const string line = """
            {"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t","content":[{"type":"text","text":"captura"},{"type":"image","source":{"type":"base64","data":"AAAA"}}]}]}}
            """;
        var r = Assert.IsType<AgentEvent.ToolResult>(parser.Parse(line, 1)[0]);
        Assert.Equal("captura\n[image]", r.Output); // lang-ok: "captura" is a Spanish fixture
    }

    [Fact]
    public void ResultWhoseCallWasNeverSeenIsStillAResultNotACorruptLine()
    {
        var parser = new ClaudeCodeLineParser(P(Path.Combine("x", Session + ".jsonl")));
        const string line = """
            {"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"nunca-visto","content":"ok"}]},"toolUseResult":{"filePath":"a.txt"}}
            """;
        IReadOnlyList<AgentEvent> events = parser.Parse(line, 1);
        Assert.Single(events);
        var r = Assert.IsType<AgentEvent.ToolResult>(events[0]);
        Assert.Equal("nunca-visto", r.CallId);
    }

    [Fact]
    public void NeverThrowsOnGarbage()
    {
        var parser = new ClaudeCodeLineParser(P(Path.Combine("x", Session + ".jsonl")));
        foreach (string garbage in new[]
                 {
                     "", "   ", "null", "[]", "42", "{", """{"type":5}""", """{"type":"user","message":7}""",
                     """{"type":"assistant","message":{"content":[{"type":"tool_use"}]}}""",
                 })
        {
            IReadOnlyList<AgentEvent> events = parser.Parse(garbage, 1);
            Assert.False(events.Count == 0, garbage);
        }
    }
}
