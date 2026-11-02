using AgentTrace.Core;

namespace AgentTrace.Presentation.Tree;

/// <summary>Which folders must be opened to reach some files. Pure logic, no Avalonia.</summary>
public static class TreeExpansion
{
    /// <summary>With more changed files than this the tree is not opened automatically: it would become unreadable.</summary>
    public const int AutoExpandLimit = 50;

    /// <summary>
    /// The folders the path goes through from the root to each file (the root included), from the shallowest
    /// to the deepest and without repeating. Paths outside the root are ignored.
    /// </summary>
    public static IReadOnlyList<FilePath> DirectoriesToExpand(FilePath root, IEnumerable<FilePath> targets)
    {
        var directories = new HashSet<FilePath>();
        foreach (FilePath path in targets)
        {
            if (!path.IsUnder(root)) continue;
            for (FilePath? dir = path.Parent; dir is { } d && d.IsUnder(root); dir = d.Parent)
            {
                directories.Add(d);
                if (d == root) break;
            }
        }
        return directories.OrderBy(NameCount).ThenBy(p => p).ToList();
    }

    /// <summary>Is the tree opened by itself down to the changes when loaded? Only if there are some and not too many.</summary>
    public static bool ShouldAutoExpand(int changedFiles) => changedFiles > 0 && changedFiles <= AutoExpandLimit;

    /// <summary>Components of the path without the root (Java's <c>Path.getNameCount</c>).</summary>
    private static int NameCount(FilePath path)
    {
        string value = path.Value;
        string rest = value[(Path.GetPathRoot(value)?.Length ?? 0)..];
        return rest.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
