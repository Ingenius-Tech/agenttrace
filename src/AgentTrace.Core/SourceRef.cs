namespace AgentTrace.Core;

/// <summary>Trace line an event comes from; allows re-reading it later.</summary>
public sealed record SourceRef(FilePath File, long LineNumber);
