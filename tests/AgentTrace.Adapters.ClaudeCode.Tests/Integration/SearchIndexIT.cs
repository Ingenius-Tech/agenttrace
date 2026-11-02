using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Core.Search;
using AgentTrace.Ingest;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>e2e of the search without UI: Claude Code trace → adapter → <c>SearchIndex</c>.</summary>
[Trait("Category", "Acceptance")]
public sealed class SearchIndexIT
{
    private static EventHeader H() => new(new SessionId("s"), "test", null, null, new SourceRef(new FilePath("t"), 1));

    [Fact]
    public void ARealSessionFindsEachFileItTouchedAndTheTimesAreSmall()
    {
        RealTrace.Found real = RealTrace.LocateOrSkip();
        IReadOnlyList<AgentEvent> events = SessionReader.ReadSession(real.Adapter, real.Session);
        var index = new SearchIndex();
        var indexing = Stopwatch.StartNew();
        foreach (AgentEvent e in events) index.Accept(e);
        indexing.Stop();

        // The results of a file match its FileRead/FileChanged (search by full path, ignoring accents and case)
        FilePath[] files = events.OfType<FileChanged>().Select(c => c.Path).Concat(events.OfType<FileRead>().Select(r => r.Path)).Distinct().Take(25).ToArray();
        Assert.NotEmpty(files);
        var query = Stopwatch.StartNew();
        foreach (FilePath file in files)
        {
            int expected = events.Count(e => e is FileChanged c && c.Path == file || e is FileRead r && r.Path == file);
            var kinds = new HashSet<SearchKind> { SearchKind.File };
            Assert.Equal(expected, index.Search(new SearchQuery(file.Value, kinds)).Count);
        }
        query.Stop();
        Console.WriteLine($"[REAL] search: {events.Count} events, {index.Count} entries, indexed in {indexing.ElapsedMilliseconds} ms, {files.Length} queries {query.ElapsedMilliseconds} ms");
        Assert.True(indexing.ElapsedMilliseconds < 10_000, "indexing the real session took " + indexing.ElapsedMilliseconds + " ms");
        Assert.True(query.ElapsedMilliseconds / files.Length < 500, "a query took too long");
    }

    [Fact]
    public void FiftyThousandSyntheticEventsAreIndexedAndQueriedFast()
    {
        var index = new SearchIndex();
        var indexing = Stopwatch.StartNew();
        for (int i = 0; i < 50_000; i++)
        {
            if (i % 50 == 0) index.Accept(new UserMessage(H(), "request number " + i));
            index.Accept(i % 2 == 0
                ? new AssistantText(H(), "agent response with enough text for the search to have real work, number " + i)
                : new ToolResult(H(), "c" + i, i % 7 == 0, "tool output with several lines\r\nsecond line of the output number " + i));
        }
        indexing.Stop();
        var query = Stopwatch.StartNew();
        IReadOnlyList<SearchHit> hits = index.Search(new SearchQuery("number 49999"));
        query.Stop();
        Console.WriteLine($"[SYNTH] 50,000 events: indexed in {indexing.ElapsedMilliseconds} ms, query {query.ElapsedMilliseconds} ms ({hits.Count} results)");
        Assert.NotEmpty(hits);
        Assert.True(indexing.ElapsedMilliseconds < 10_000, "indexing: " + indexing.ElapsedMilliseconds + " ms");
        Assert.True(query.ElapsedMilliseconds < 500, "query: " + query.ElapsedMilliseconds + " ms");
    }
}
