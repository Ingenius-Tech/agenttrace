using AgentTrace.Core;
using AgentTrace.Ingest;

namespace AgentTrace.Presentation.Tree;

/// <summary>Lists the content of a folder for the tree: what is on disk plus the deleted entries that are still in the change list.</summary>
public static class DirectoryLister
{
    /// <summary>Folders first, then by name ignoring case. Never throws: an unreadable folder comes out empty.</summary>
    public static IReadOnlyList<FileNode> Children(FilePath dir, IgnoreRules rules, FileStatusIndex index)
    {
        List<FileNode> nodes = [];
        HashSet<string> onDisk = [];
        try
        {
            // FileSystemInfo brings attributes and size from the enumeration itself: no extra system call per entry
            foreach (FileSystemInfo entry in new DirectoryInfo(dir.Value).EnumerateFileSystemInfos())
            {
                var child = new FilePath(entry.FullName);
                if (rules.IsIgnored(child)) continue;
                onDisk.Add(child.FileName);
                nodes.Add(Describe(child, entry));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // unreadable or vanished folder: it is shown empty and the next refresh will fix it
        }
        foreach (string name in index.ChildNamesOnChangedPaths(dir))
        {
            if (onDisk.Contains(name)) continue;
            FilePath ghost = dir.Combine(name);
            if (rules.IsIgnored(ghost)) continue;
            bool folder = index.ChildNamesOnChangedPaths(ghost).Count > 0;
            nodes.Add(new FileNode(ghost, folder, true, false));
        }
        return [.. nodes
            .OrderBy(n => !n.Directory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Name, StringComparer.Ordinal)];
    }

    private static FileNode Describe(FilePath path, FileSystemInfo entry)
    {
        try
        {
            FileAttributes attrs = entry.Attributes;
            // Symbolic links and Windows junctions are reparse points: they are not followed
            bool link = attrs.HasFlag(FileAttributes.ReparsePoint);
            bool directory = attrs.HasFlag(FileAttributes.Directory) || (link && Directory.Exists(path.Value));
            return new FileNode(path, directory, false, link, directory || entry is not FileInfo file ? 0 : file.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new FileNode(path, false, false, false);
        }
    }
}
