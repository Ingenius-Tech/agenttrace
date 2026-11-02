namespace AgentTrace.Core;

/// <summary>A file change and the agent action it is attributed to.</summary>
public sealed record ChangeEntry(FileChange Change, Attribution Attribution);
