namespace AgentTrace.Core;

/// <summary>
/// Extension point to support a coding agent. It is registered in the application catalog
/// (<c>AdapterCatalog</c>, in Desktop); the core does not discover adapters by reflection.
/// </summary>
public interface IAgentAdapter
{
    /// <summary>Stable name of the agent, for example <c>claude-code</c>.</summary>
    string Agent { get; }

    /// <summary>Whether the file is a trace of this agent.</summary>
    bool Owns(FilePath traceFile);

    /// <summary>Session traces of the project, from newest to oldest. Empty if the agent has no traces for it.</summary>
    IReadOnlyList<FilePath> FindTraceFiles(FilePath projectDir);

    /// <summary>Traces associated with a session (for example, those of its subagents). None by default.</summary>
    IReadOnlyList<FilePath> RelatedTraceFiles(FilePath sessionTrace) => [];

    ILineParser NewParser(FilePath traceFile);
}
