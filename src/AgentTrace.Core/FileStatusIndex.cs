namespace AgentTrace.Core;

/// <summary>
/// Status of each file of the project and summary of each folder, computed from the changes and the reads
/// of the agent. It is immutable: it is rebuilt with each list of changes. It only contains paths inside the project.
/// </summary>
public sealed class FileStatusIndex
{
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>();

    private readonly Dictionary<FilePath, FileStatus> _files;
    private readonly Dictionary<FilePath, FolderSummary> _folders;
    private readonly Dictionary<FilePath, HashSet<string>> _childrenOnChangedPaths;
    private readonly HashSet<FilePath> _readPaths;

    private FileStatusIndex(FilePath root, Dictionary<FilePath, FileStatus> files, Dictionary<FilePath, FolderSummary> folders,
        Dictionary<FilePath, HashSet<string>> children, HashSet<FilePath> readPaths)
    {
        Root = root;
        _files = files;
        _folders = folders;
        _childrenOnChangedPaths = children;
        _readPaths = readPaths;
    }

    public FilePath Root { get; }

    public static FileStatusIndex Empty(FilePath projectDir) => Of(projectDir, [], []);

    public static FileStatusIndex Of(FilePath projectDir, IEnumerable<ChangeEntry> changes, IEnumerable<FilePath> reads)
    {
        FilePath root = projectDir;
        var files = new Dictionary<FilePath, FileStatus>();
        var folders = new Dictionary<FilePath, FolderSummary>();
        var children = new Dictionary<FilePath, HashSet<string>>();
        var readInside = new HashSet<FilePath>();

        foreach (FilePath path in reads)
        {
            if (!path.IsUnder(root) || path == root) continue;
            readInside.Add(path);
            files[path] = new FileStatus(null, Actor.Agent, true);
        }
        foreach (ChangeEntry entry in changes)
        {
            FilePath path = entry.Change.Path;
            if (!path.IsUnder(root) || path == root) continue;
            Actor actor = Actors.Of(entry.Attribution);
            bool wasRead = readInside.Contains(path);
            files[path] = new FileStatus(entry.Change.Kind, actor, wasRead);
            bool external = actor == Actor.External;
            FilePath child = path;
            for (FilePath? dir = path.Parent; dir is { } d && d.IsUnder(root); dir = d.Parent)
            {
                folders[d] = folders.GetValueOrDefault(d, FolderSummary.Empty).Plus(entry.Change.Kind, external);
                AddChild(children, d, child);
                child = d;
                if (d == root) break;
            }
        }
        // Folders with reads must also be findable (for "changes and reads only")
        foreach (FilePath read in readInside)
        {
            FilePath child = read;
            for (FilePath? dir = read.Parent; dir is { } d && d.IsUnder(root); dir = d.Parent)
            {
                AddChild(children, d, child);
                child = d;
                if (d == root) break;
            }
        }
        return new FileStatusIndex(root, files, folders, children, readInside);
    }

    private static void AddChild(Dictionary<FilePath, HashSet<string>> children, FilePath dir, FilePath child)
    {
        if (!children.TryGetValue(dir, out HashSet<string>? names)) children[dir] = names = [];
        names.Add(child.FileName);
    }

    /// <summary>Status of the file, or null if nothing is known about it.</summary>
    public FileStatus? File(FilePath path) => _files.GetValueOrDefault(path);

    /// <summary>Summary of the changes below the folder; empty if there are none.</summary>
    public FolderSummary Folder(FilePath dir) => _folders.GetValueOrDefault(dir, FolderSummary.Empty);

    /// <summary>Names of the folder's children that are a change or lead to one (to show what was deleted, which is no longer on disk).</summary>
    public IReadOnlySet<string> ChildNamesOnChangedPaths(FilePath dir) =>
        _childrenOnChangedPaths.TryGetValue(dir, out HashSet<string>? names) ? names : NoNames;

    /// <summary>Changed files, sorted by path (does not include those that were only read).</summary>
    public IReadOnlyList<FilePath> ChangedPaths() =>
        _files.Where(kv => kv.Value.Changed).Select(kv => kv.Key).Order().ToList();

    /// <summary>Files the agent has read in the session, sorted by path.</summary>
    public IReadOnlyList<FilePath> ReadPaths() => _readPaths.Order().ToList();
}
