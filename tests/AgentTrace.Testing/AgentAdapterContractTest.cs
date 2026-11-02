using AgentTrace.Core;

namespace AgentTrace.Testing;

/// <summary>
/// Contract every <see cref="IAgentAdapter"/> must fulfil. To test a new adapter it is enough to
/// inherit from this class and implement the three abstract methods (see docs/ADAPTERS.md).
/// </summary>
public abstract class AgentAdapterContractTest
{
    protected abstract IAgentAdapter Adapter();

    /// <summary>A sample trace of the agent, with several lines.</summary>
    protected abstract FilePath SampleTrace();

    /// <summary>A project for which <see cref="Adapter"/> finds traces (including <see cref="SampleTrace"/>).</summary>
    protected abstract FilePath ProjectWithTraces();

    [Fact]
    public void HasAStableNonBlankName()
    {
        Assert.False(string.IsNullOrWhiteSpace(Adapter().Agent));
        Assert.Equal(Adapter().Agent, Adapter().Agent);
    }

    [Fact]
    public void OwnsItsTracesAndNothingElse()
    {
        Assert.True(Adapter().Owns(SampleTrace()));
        Assert.False(Adapter().Owns(new FilePath("not-a-trace.txt")));
    }

    [Fact]
    public void CreatesAnIndependentParserPerCall()
    {
        Assert.NotSame(Adapter().NewParser(SampleTrace()), Adapter().NewParser(SampleTrace()));
    }

    [Fact]
    public void EveryLineProducesEventsCarryingTheirOrigin()
    {
        IAgentAdapter adapter = Adapter();
        ILineParser parser = adapter.NewParser(SampleTrace());
        string[] lines = File.ReadAllLines(SampleTrace().Value);
        Assert.NotEmpty(lines);
        long n = 0;
        foreach (string line in lines)
        {
            n++;
            IReadOnlyList<AgentEvent> events = parser.Parse(line, n);
            Assert.False(events.Count == 0, "line " + n + " without events");
            foreach (AgentEvent e in events)
            {
                EventHeader h = e.Header;
                Assert.Equal(adapter.Agent, h.Agent);
                Assert.NotNull(h.SessionId);
                Assert.Equal(n, h.Source.LineNumber);
            }
        }
    }

    [Fact]
    public void EveryToolCallIsClassifiedAndSomeAreNotJustOther()
    {
        ILineParser parser = Adapter().NewParser(SampleTrace());
        var calls = new List<AgentEvent.ToolCall>();
        long n = 0;
        foreach (string line in File.ReadAllLines(SampleTrace().Value))
        {
            foreach (AgentEvent e in parser.Parse(line, ++n))
            {
                if (e is AgentEvent.ToolCall call) calls.Add(call);
            }
        }
        Assert.False(calls.Count == 0, "the sample trace must have tool calls");
        Assert.True(calls.All(c => c.Summary != null));
        Assert.True(calls.Any(c => c.Kind != AgentEvent.ToolKind.Other),
            "the adapter must classify the tools it knows, not leave them all as Other");
    }

    [Fact]
    public void DelegatedCallsAreLinkedToTheirSubagent()
    {
        ILineParser parser = Adapter().NewParser(SampleTrace());
        bool delegates = false;
        bool linked = false;
        long n = 0;
        foreach (string line in File.ReadAllLines(SampleTrace().Value))
        {
            foreach (AgentEvent e in parser.Parse(line, ++n))
            {
                if (e is AgentEvent.ToolCall { Kind: AgentEvent.ToolKind.Delegate }) delegates = true;
                if (e is AgentEvent.SubagentStarted s)
                {
                    linked = true;
                    Assert.False(string.IsNullOrWhiteSpace(s.SubagentId));
                }
            }
        }
        Assert.True(!delegates || linked, "if the trace delegates to subagents, the adapter must emit SubagentStarted");
    }

    [Fact]
    public void ParserNeverThrowsOnGarbage()
    {
        ILineParser parser = Adapter().NewParser(SampleTrace());
        foreach (string garbage in new[] { "", " ", "{", "null", "[1,2]", "\u0000", "{\"type\":null}" })
        {
            Assert.False(parser.Parse(garbage, 1).Count == 0, garbage);
        }
    }

    [Fact]
    public void FindsTracesForAProjectAndNothingForAnUnknownOne()
    {
        IAgentAdapter adapter = Adapter();
        FilePath sample = SampleTrace();
        Assert.Contains(sample, adapter.FindTraceFiles(ProjectWithTraces()));
        Assert.Empty(adapter.FindTraceFiles(new FilePath("nonexistent-project-xyz")));
    }
}
