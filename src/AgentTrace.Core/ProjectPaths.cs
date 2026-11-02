namespace AgentTrace.Core;

/// <summary>Which project paths the tracking views (Knowledge graph and Heatmap) follow.</summary>
public static class ProjectPaths
{
    /// <summary>
    /// True if some segment of <paramref name="path"/> relative to <paramref name="root"/> (a folder or the file itself) has
    /// a name that starts with a dot: <c>.git</c>, <c>.claude</c>, <c>.gitignore</c>, <c>src/.cache/x</c>... Those are tooling
    /// and environment files, not the project, and they would drown out its activity. Only the part below the root counts, so a
    /// project that lives under a dotted folder is not hidden as a whole.
    /// </summary>
    public static bool IsHidden(FilePath root, FilePath path)
    {
        for (FilePath? p = path; p is { } current && current != root && current.IsUnder(root); p = current.Parent)
        {
            if (current.FileName.StartsWith('.')) return true;
        }
        return false;
    }
}
