using AgentTrace.Core;

namespace AgentTrace.Core.Tests;

public sealed class CoreHasNoDependenciesTest
{
    [Fact]
    public void CoreAssemblyReferencesNoAgentTraceOrAvaloniaAssembly()
    {
        var core = typeof(AgentEvent).Assembly;

        var forbidden = core.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("AgentTrace.", StringComparison.Ordinal)
                        || n.StartsWith("Avalonia", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }
}
