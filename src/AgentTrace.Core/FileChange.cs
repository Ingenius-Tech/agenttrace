using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core;

/// <summary>
/// A file that has changed in the project relative to the baseline. <c>ModifiedAt</c> is the last
/// modification on disk (UTC), or null if the file was deleted.
/// </summary>
public sealed record FileChange(FilePath Path, ChangeKind Kind, DateTimeOffset? ModifiedAt);
