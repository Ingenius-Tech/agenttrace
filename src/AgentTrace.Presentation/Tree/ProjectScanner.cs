using AgentTrace.Core;
using AgentTrace.Ingest;

namespace AgentTrace.Presentation.Tree;

/// <summary>A file of the listing. <c>Ghost</c>: no longer exists on disk but is still in the change list (its size is 0).</summary>
public sealed record ListedFile(FilePath Path, long Size, bool Ghost);

/// <summary>Files of the project on disk. <c>Truncated</c>: the limit was reached and there are more unlisted.</summary>
public sealed record ProjectListing(ValueList<ListedFile> Files, bool Truncated);

/// <summary>Walks the project breadth-first with the same rules as the tree (<see cref="DirectoryLister"/>) and records the size of each file (which the listing already brings).</summary>
public static class ProjectScanner
{
    public const int DefaultMaxFiles = 20_000;

    /// <summary>Without following links and skipping files and folders whose name starts with a dot. Never throws: what cannot be read is skipped or comes out with size 0.</summary>
    public static ProjectListing Scan(FilePath root, IgnoreRules rules, FileStatusIndex index, int maxFiles)
    {
        List<ListedFile> files = [];
        Queue<FilePath> pending = new([root]);
        while (pending.Count > 0)
        {
            foreach (FileNode node in DirectoryLister.Children(pending.Dequeue(), rules, index))
            {
                if (ProjectPaths.IsHidden(root, node.Path)) continue; // dotted files and folders are not followed (see ProjectPaths)
                if (node.Expandable)
                {
                    pending.Enqueue(node.Path);
                }
                else if (!node.Directory)
                {
                    if (files.Count >= maxFiles) return new ProjectListing(files.ToValueList(), true);
                    files.Add(new ListedFile(node.Path, node.Size, node.Ghost));
                }
            }
        }
        return new ProjectListing(files.ToValueList(), false);
    }
}
