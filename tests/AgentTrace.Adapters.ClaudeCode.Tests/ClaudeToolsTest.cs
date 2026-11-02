using System.Text.Json;
using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode.Tests;

public sealed class ClaudeToolsTest
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void ClassifiesEveryKnownTool()
    {
        foreach (string tool in new[] { "Read", "NotebookRead", "WebFetch" }) Assert.True(AgentEvent.ToolKind.Read == ClaudeTools.KindOf(tool), tool);
        foreach (string tool in new[] { "Write", "Edit", "MultiEdit", "NotebookEdit" }) Assert.True(AgentEvent.ToolKind.Write == ClaudeTools.KindOf(tool), tool);
        foreach (string tool in new[] { "Bash", "PowerShell" }) Assert.True(AgentEvent.ToolKind.Execute == ClaudeTools.KindOf(tool), tool);
        foreach (string tool in new[] { "Grep", "Glob", "WebSearch" }) Assert.True(AgentEvent.ToolKind.Search == ClaudeTools.KindOf(tool), tool);
        foreach (string tool in new[] { "Agent", "Task" }) Assert.True(AgentEvent.ToolKind.Delegate == ClaudeTools.KindOf(tool), tool);
    }

    [Fact]
    public void McpToolsAreClassifiedByTheirFunction()
    {
        Assert.Equal(AgentEvent.ToolKind.Execute, ClaudeTools.KindOf("mcp__plugin_context-mode_context-mode__ctx_execute"));
        Assert.Equal(AgentEvent.ToolKind.Execute, ClaudeTools.KindOf("mcp__plugin_context-mode_context-mode__ctx_batch_execute"));
        Assert.Equal(AgentEvent.ToolKind.Search, ClaudeTools.KindOf("mcp__plugin_context-mode_context-mode__ctx_search"));
    }

    [Fact]
    public void UnknownToolsAreOther()
    {
        Assert.Equal(AgentEvent.ToolKind.Other, ClaudeTools.KindOf("SubagentHandback"));
        Assert.Equal(AgentEvent.ToolKind.Other, ClaudeTools.KindOf("TodoWrite"));
        Assert.Equal(AgentEvent.ToolKind.Other, ClaudeTools.KindOf("mcp__other__thing"));
        Assert.Equal(AgentEvent.ToolKind.Other, ClaudeTools.KindOf(""));
    }

    [Fact]
    public void SummaryPicksTheMostMeaningfulFieldForEachKind()
    {
        Assert.Equal("C:/p/a.txt", ClaudeTools.Summary(AgentEvent.ToolKind.Write, Json("""{"file_path":"C:/p/a.txt","content":"x"}""")));
        Assert.Equal("src/B.java", ClaudeTools.Summary(AgentEvent.ToolKind.Read, Json("""{"file_path":"src/B.java","offset":1}""")));
        Assert.Equal("git status", ClaudeTools.Summary(AgentEvent.ToolKind.Execute, Json("""{"command":"git status"}""")));
        Assert.Equal("ls -la", ClaudeTools.Summary(AgentEvent.ToolKind.Execute, Json("""{"language":"shell","code":"ls -la"}""")));
        Assert.Equal("TODO", ClaudeTools.Summary(AgentEvent.ToolKind.Search, Json("""{"pattern":"TODO","path":"src"}""")));
        Assert.Equal("search x", ClaudeTools.Summary(AgentEvent.ToolKind.Search, Json("""{"queries":["search x","another"]}""")));
        Assert.Equal("investigate", ClaudeTools.Summary(AgentEvent.ToolKind.Delegate, Json("""{"description":"investigate","prompt":"veryLong"}""")));
    }

    [Fact]
    public void SummaryIsASingleShortLine()
    {
        Assert.Equal("first line", ClaudeTools.Summary(AgentEvent.ToolKind.Execute, Json("""{"command":"first line\nsecond"}""")));
        string longCommand = new('x', 500);
        string summary = ClaudeTools.Summary(AgentEvent.ToolKind.Execute, Json("{\"command\":\"" + longCommand + "\"}"));
        Assert.True(summary.Length <= 121 && summary.EndsWith('…'), "length " + summary.Length);
    }

    [Fact]
    public void SummaryIsEmptyWhenThereIsNothingToShow()
    {
        Assert.Equal("", ClaudeTools.Summary(AgentEvent.ToolKind.Other, Json("{}")));
        Assert.Equal("", ClaudeTools.Summary(AgentEvent.ToolKind.Read, Json("""{"offset":3}""")));
        Assert.Equal("", ClaudeTools.Summary(AgentEvent.ToolKind.Execute, Json("null")));
    }
}
