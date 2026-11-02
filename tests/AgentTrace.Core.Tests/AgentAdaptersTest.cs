namespace AgentTrace.Core.Tests;

public sealed class AgentAdaptersTest
{
    private sealed class FakeAdapter(string agent, string extension) : IAgentAdapter
    {
        public string Agent => agent;

        public bool Owns(FilePath traceFile) => traceFile.FileName.EndsWith(extension, StringComparison.Ordinal);

        public IReadOnlyList<FilePath> FindTraceFiles(FilePath projectDir) => [];

        public ILineParser NewParser(FilePath traceFile) => throw new NotSupportedException();
    }

    // Divergence: in Java, ServiceLoader discovered the real Claude Code adapter (the test lived in the core).
    // The C# core knows no concrete adapters: it receives them via constructor, and that the application's catalog
    // includes "claude-code" is checked by the adapter's own test (F5) or by AdapterCatalog's (F8).
    [Fact]
    public void DiscoversRegisteredAdapters()
    {
        var adapters = new AgentAdapters([new FakeAdapter("claude-code", ".jsonl")]);

        Assert.Contains(adapters.All, a => a.Agent == "claude-code");
    }

    [Fact]
    public void AgentNamesAreUnique()
    {
        var adapters = new AgentAdapters([new FakeAdapter("a", ".a"), new FakeAdapter("b", ".b")]);

        var names = adapters.All.Select(a => a.Agent).ToList();
        Assert.True(names.Count == names.Distinct().Count());
    }

    [Fact]
    public void ForTraceReturnsTheAdapterThatOwnsTheFileOrNull()
    {
        var a = new FakeAdapter("a", ".a");
        var b = new FakeAdapter("b", ".b");
        var adapters = new AgentAdapters([a, b]);

        Assert.Same(b, adapters.ForTrace(new FilePath("x.b")));
        Assert.Null(adapters.ForTrace(new FilePath("x.c")));
    }

    [Fact]
    public void RelatedTraceFilesIsEmptyByDefault()
    {
        IAgentAdapter adapter = new FakeAdapter("a", ".a");

        Assert.Empty(adapter.RelatedTraceFiles(new FilePath("x.a")));
    }
}
