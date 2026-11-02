using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core;

/// <summary>
/// Changes contained in a folder (at any level below it).
/// <c>External</c>: how many of them are not attributed to the agent (already counted in their kind).
/// </summary>
public sealed record FolderSummary(int Created, int Modified, int Deleted, int External)
{
    public static readonly FolderSummary Empty = new(0, 0, 0, 0);

    public int Total => Created + Modified + Deleted;

    /// <summary>The kind that deserves the most attention when reviewing: deleted, then modified, then created; null if there are no changes.</summary>
    public ChangeKind? Strongest()
    {
        if (Deleted > 0) return ChangeKind.Deleted;
        if (Modified > 0) return ChangeKind.Modified;
        if (Created > 0) return ChangeKind.Created;
        return null;
    }

    internal FolderSummary Plus(ChangeKind kind, bool isExternal) => new(
        Created + (kind == ChangeKind.Created ? 1 : 0),
        Modified + (kind == ChangeKind.Modified ? 1 : 0),
        Deleted + (kind == ChangeKind.Deleted ? 1 : 0),
        External + (isExternal ? 1 : 0));
}
