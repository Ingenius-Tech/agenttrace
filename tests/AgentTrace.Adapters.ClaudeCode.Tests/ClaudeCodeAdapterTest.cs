using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode.Tests;

public sealed class ClaudeCodeAdapterTest
{
    private static FilePath Root() => TestSupport.Resource("claude/projects");

    [Fact]
    public void OwnsSessionAndSubagentTracesUnderItsRoot()
    {
        var adapter = new ClaudeCodeAdapter(Root(), false);
        FilePath session = Root().Combine("D--fake-proj/aaaaaaaa-0000-0000-0000-000000000001.jsonl");
        FilePath sub = Root().Combine("D--fake-proj/aaaaaaaa-0000-0000-0000-000000000001/subagents/agent-abc123.jsonl");
        Assert.True(adapter.Owns(session));
        Assert.True(adapter.Owns(sub));
        Assert.False(adapter.Owns(Root().Combine("D--fake-proj/notes.jsonl")));
        Assert.False(adapter.Owns(new FilePath(Path.Combine("other-folder", "aaaaaaaa-0000-0000-0000-000000000001.jsonl"))));
    }

    [Fact]
    public void NamesTheAgentAndCreatesAParserPerFile()
    {
        var adapter = new ClaudeCodeAdapter(Root(), false);
        Assert.Equal("claude-code", adapter.Agent);
        Assert.Equal("claude-code", new ClaudeCodeAdapter().Agent);
        FilePath f = Root().Combine("D--fake-proj/aaaaaaaa-0000-0000-0000-000000000001.jsonl");
        Assert.NotSame(adapter.NewParser(f), adapter.NewParser(f));
    }
}
