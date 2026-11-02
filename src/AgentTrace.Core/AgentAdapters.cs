namespace AgentTrace.Core;

/// <summary>
/// Available agent adapters. In Java they were discovered by <c>ServiceLoader</c>; here they come through the constructor
/// (the only place that knows concrete adapters is <c>AdapterCatalog</c>, in Desktop).
/// </summary>
public sealed class AgentAdapters(IReadOnlyList<IAgentAdapter> adapters)
{
    public IReadOnlyList<IAgentAdapter> All { get; } = adapters.ToArray();

    /// <summary>Adapter that recognises the given trace, if any.</summary>
    public IAgentAdapter? ForTrace(FilePath traceFile) => All.FirstOrDefault(a => a.Owns(traceFile));
}
