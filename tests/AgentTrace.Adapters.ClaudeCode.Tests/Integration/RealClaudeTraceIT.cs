using System.Globalization;
using System.Text.Json;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Ingest;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// Acceptance against a REAL Claude Code trace. It does not copy the trace into the repository.
/// Source: the <c>AGENTTRACE_REAL_TRACE</c> variable; if unset, the most recent session of this same project
/// in <c>~/.claude/projects</c>. Skipped if none exists.
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class RealClaudeTraceIT
{
    [Fact]
    public void ProcessesARealSessionWithoutLosingAnything()
    {
        RealTrace.Found real = RealTrace.LocateOrSkip();
        FilePath session = real.Session;
        IReadOnlyList<AgentEvent> events = SessionReader.ReadSession(real.Adapter, session);
        IReadOnlyList<FilePath> related = real.Adapter.RelatedTraceFiles(session);

        // No line is lost: the line numbers of each file are consecutive from 1
        foreach (IGrouping<FilePath, AgentEvent> file in events.GroupBy(e => e.Header.Source.File))
        {
            HashSet<long> lines = [.. file.Select(e => e.Header.Source.LineNumber)];
            Assert.True(lines.Max() == lines.Count, "lines without events in " + file.Key);
        }

        var stats = new SessionStats();
        foreach (AgentEvent e in events) stats.Accept(e);

        // No corrupt lines, and every result linked to its call (within the same file)
        Assert.Equal(0, stats.SkippedCount(SkipReason.Malformed)); // corrupt lines
        Dictionary<FilePath, HashSet<string>> callsPerFile = events.OfType<ToolCall>()
            .GroupBy(c => c.Header.Source.File).ToDictionary(g => g.Key, g => g.Select(c => c.CallId).ToHashSet());
        foreach (ToolResult r in events.OfType<ToolResult>())
        {
            Assert.True(callsPerFile.TryGetValue(r.Header.Source.File, out HashSet<string>? ids) && ids.Contains(r.CallId),
                "result without call: " + r.CallId);
        }

        // Tokens: the sum deduplicated per message never exceeds the naive per-line sum and is positive
        long naive = NaiveOutputTokens(session);
        foreach (FilePath r in related) naive += NaiveOutputTokens(r);
        Assert.True(stats.OutputTokens > 0, "no output tokens");
        Assert.True(stats.OutputTokens <= naive, "deduplication cannot increase the tokens");
        Assert.True(stats.Turns >= 1, "no finished turn");
        Assert.Contains(events, e => e is UserMessage);
        Assert.Contains(events, e => e is AssistantText);

        if (real.OwnProject)
        {
            // Checks specific to this project's own development session
            Assert.True(stats.ToolCalls.ContainsKey("Bash"), "Bash does not appear");
            Assert.True(stats.ToolCalls.ContainsKey("Write"), "Write does not appear");
            Assert.True(stats.ToolCalls.ContainsKey("Agent"), "Agent does not appear");
            Assert.Contains(events, e => e is FileRead);
            Assert.NotEmpty(stats.FilesRead);
            HashSet<string> touched = [.. stats.FilesTouched.Select(p => p.FileName)];
            Assert.True(touched.Contains("CLAUDE.md"), "CLAUDE.md is not listed as touched: " + string.Join(", ", touched));
            // DEVLOG.md is a local, git-ignored working file: only expected where it exists
            if (File.Exists(Path.Combine(TestPaths.RepoRoot().Value, "DEVLOG.md")))
                Assert.True(touched.Contains("DEVLOG.md"), "DEVLOG.md is not listed as touched: " + string.Join(", ", touched));
            Assert.NotEmpty(related); // subagents were expected
            Assert.Contains(events, e => e.Header.SubagentId is not null);
        }

        PrintSummary(session, related, events, stats, naive);
    }

    /// <summary>
    /// Heatmap: every file read or write call carries its <c>Target</c>, errors on project files
    /// are placed in the graph (as many as an independent count of the trace) and the rest are counted in <c>UnplacedErrors</c>.
    /// </summary>
    [Fact]
    public void FileToolCallsCarryATargetAndTheirErrorsLandOnTheFile()
    {
        RealTrace.Found real = RealTrace.LocateOrSkip();
        IReadOnlyList<AgentEvent> events = SessionReader.ReadSession(real.Adapter, real.Session);
        FilePath root = real.ProjectRoot;
        string[] fileTools = ["Read", "Edit", "Write", "MultiEdit", "NotebookEdit"];

        // Every Read/Edit/Write with a path in its input carries a Target
        List<ToolCall> fileCalls = [.. events.OfType<ToolCall>().Where(c => fileTools.Contains(c.ToolName))];
        Assert.All(fileCalls, c => Assert.True(c.Target is not null, "no Target: " + c.ToolName + " " + c.InputJson));
        Assert.Contains(fileCalls, c => c.ToolName == "Read");

        // Independent count: error results of calls whose Target is inside the project, and the other errors
        Dictionary<string, ToolCall> byId = events.OfType<ToolCall>().GroupBy(c => c.CallId).ToDictionary(g => g.Key, g => g.First());
        int expectedPlaced = 0, expectedUnplaced = 0;
        foreach (ToolResult r in events.OfType<ToolResult>().Where(r => r.Error))
        {
            if (!byId.TryGetValue(r.CallId, out ToolCall? call)) continue; // without a known call it is ignored
            if (call.Target is { } t && t.IsUnder(root)) expectedPlaced++;
            else expectedUnplaced++;
        }

        var builder = new KnowledgeBuilder(root);
        foreach (AgentEvent e in events) builder.Accept(e);
        KnowledgeGraph graph = builder.Snapshot();
        Assert.True(graph.TargetsKnown);
        Assert.Equal(expectedPlaced, graph.Nodes.Sum(n => n.Errors));
        Assert.Equal(expectedUnplaced, graph.UnplacedErrors);
        Console.WriteLine("[REAL] errors per file: placed=" + expectedPlaced + " noFile(UnplacedErrors)=" + graph.UnplacedErrors
            + " filesWithErrors=" + graph.Nodes.Count(n => n.Errors > 0));
        if (expectedPlaced == 0) Console.WriteLine("[REAL] the trace has no failed Read/Edit/Write call on a project file");
    }

    /// <summary>Sum of output_tokens counting every assistant line, as a naive reader would.</summary>
    private static long NaiveOutputTokens(FilePath file)
    {
        long sum = 0;
        foreach (string line in File.ReadAllLines(file.Value))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement n = doc.RootElement;
                if (n.ValueKind == JsonValueKind.Object && n.TryGetProperty("type", out JsonElement type) && type.GetString() == "assistant"
                    && n.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.Object
                    && message.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object
                    && usage.TryGetProperty("output_tokens", out JsonElement tokens) && tokens.TryGetInt64(out long value))
                {
                    sum += value;
                }
            }
            catch (JsonException)
            {
                // partial line at the end of a trace that is still being written
            }
        }
        return sum;
    }

    private static string Map<TValue>(IEnumerable<KeyValuePair<string, TValue>> entries) =>
        "{" + string.Join(", ", entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "=" + e.Value)) + "}";

    private static void PrintSummary(FilePath session, IReadOnlyList<FilePath> related, IReadOnlyList<AgentEvent> events, SessionStats s,
        long naiveOutput)
    {
        Dictionary<string, int> byType = events.GroupBy(e => e.GetType().Name).ToDictionary(g => g.Key, g => g.Count());
        HashSet<string> unknown = [.. events.OfType<Skipped>().Where(k => k.Reason == SkipReason.Unknown).Select(k => k.RawType)];
        Console.WriteLine("[REAL] trace: " + session.FileName + " (+" + related.Count + " subagents)");
        Console.WriteLine("[REAL] events: " + events.Count + " " + Map(byType));
        Console.WriteLine("[REAL] tokens: input=" + s.InputTokens + " output=" + s.OutputTokens
            + " (naive per line=" + naiveOutput + ") cacheRead=" + s.CacheReadTokens
            + " cacheCreation=" + s.CacheCreationTokens);
        Console.WriteLine("[REAL] tools: " + Map(s.ToolCalls) + " errors=" + s.ToolErrors);
        Console.WriteLine("[REAL] files read: " + s.FilesRead.Count);
        Console.WriteLine("[REAL] files touched: " + s.FilesTouched.Count + " (external: " + s.FilesChangedExternally.Count + ")");
        // Java printed the duration as java.time.Duration (PT1M2.5S); here, in seconds
        Console.WriteLine("[REAL] turns: " + s.Turns + " duration=" + s.TurnTime.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s");
        Console.WriteLine("[REAL] discarded: ignored=" + s.SkippedCount(SkipReason.Ignored) + " unknown="
            + s.SkippedCount(SkipReason.Unknown) + " [" + string.Join(", ", unknown) + "] corrupt=" + s.SkippedCount(SkipReason.Malformed));
    }
}
