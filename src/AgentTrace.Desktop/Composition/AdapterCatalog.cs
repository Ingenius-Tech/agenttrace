using AgentTrace.Adapters.ClaudeCode;
using AgentTrace.Core;

namespace AgentTrace.Desktop.Composition;

/// <summary>
/// The composition root: the only place that knows the concrete adapters. There is no discovery by reflection
/// (it would complicate the single-file executable). Adding an agent is a project <c>AgentTrace.Adapters.&lt;Agent&gt;</c>
/// and one line here (see <c>docs/ADAPTERS.md</c>).
/// </summary>
public static class AdapterCatalog
{
    public static IReadOnlyList<IAgentAdapter> All() => [new ClaudeCodeAdapter()];
}
