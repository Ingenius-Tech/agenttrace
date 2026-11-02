using AgentTrace.Core;

namespace AgentTrace.Ingest;

/// <summary>
/// Notifies that something has changed in a project. File system events are not interpreted one by one
/// (they get lost, duplicated and arrive in bursts): they only trigger a notice, and whoever receives it queries the real state.
/// If the system overflows its event queue, a notice is also raised.
/// </summary>
public sealed class LocalFileSystemWatcher(Action onChange) : IDisposable
{
    private const int BufferSize = 64 * 1024;

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _gate = new();
    private volatile bool _disposed;

    /// <summary>
    /// Watches the project recursively and, without recursion, each extra folder (for example, git's
    /// data directory, to learn about commits and branch changes).
    /// </summary>
    public void StartWatching(FilePath projectDir, params FilePath[] extraDirectories)
    {
        // A single recursive watcher cannot exclude folders: events falling in the omitted ones are filtered out
        Add(projectDir, recursive: true, root: projectDir);
        foreach (FilePath extra in extraDirectories) Watch(extra);
    }

    /// <summary>Also watches a folder, without recursion (for example, git's data directory once the repository is located).</summary>
    public void Watch(FilePath dir) => Add(dir, recursive: false, root: null);

    private void Add(FilePath dir, bool recursive, FilePath? root)
    {
        var watcher = new FileSystemWatcher(dir.Value)
        {
            IncludeSubdirectories = recursive,
            InternalBufferSize = BufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                           | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        void Handle(string path)
        {
            if (_disposed) return;
            if (root is { } r && IsNoise(r, path)) return;
            onChange();
        }
        watcher.Created += (_, e) => Handle(e.FullPath);
        watcher.Changed += (_, e) => Handle(e.FullPath);
        watcher.Deleted += (_, e) => Handle(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            Handle(e.OldFullPath);
            Handle(e.FullPath);
        };
        // Event queue overflow: changes were lost, so it also counts as a notice
        watcher.Error += (_, _) =>
        {
            if (!_disposed) onChange();
        };
        lock (_gate)
        {
            if (_disposed) return;
            _watchers.Add(watcher);
        }
        watcher.EnableRaisingEvents = true;
    }

    /// <summary>Events inside omitted folders (or on them): they are not changes.</summary>
    private static bool IsNoise(FilePath root, string path)
    {
        var full = new FilePath(path);
        if (!full.IsUnder(root)) return false;
        string relative = full.RelativeTo(root);
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (IgnoreRules.FallbackDirectories.Contains(part)) return true;
        }
        return false;
    }

    public void Dispose()
    {
        FileSystemWatcher[] toDispose;
        lock (_gate)
        {
            _disposed = true;
            toDispose = [.. _watchers];
            _watchers.Clear();
        }
        foreach (FileSystemWatcher watcher in toDispose)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }
}
