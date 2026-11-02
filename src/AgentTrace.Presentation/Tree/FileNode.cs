using AgentTrace.Core;

namespace AgentTrace.Presentation.Tree;

/// <summary>
/// An entry of the file tree.
/// <c>Directory</c>: shown as a folder (a link to a folder too, but it is not expanded).
/// <c>Ghost</c>: no longer exists on disk but is still in the change list (it was deleted).
/// <c>Symlink</c>: symbolic link or junction: not followed.
/// <c>Size</c>: bytes of a file on disk (0 for folders, deleted entries or when it could not be read).
/// </summary>
public sealed record FileNode(FilePath Path, bool Directory, bool Ghost, bool Symlink, long Size = 0)
{
    public string Name => Path.FileName.Length == 0 ? Path.ToString() : Path.FileName;

    public bool Expandable => Directory && !Symlink;
}
