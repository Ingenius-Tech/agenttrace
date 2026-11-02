using AgentTrace.Core;
using AgentTrace.Diff;
using System.Text;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Git;

/// <summary>
/// Changes of a project according to git. Read-only: it uses <c>diff</c>, <c>ls-files</c> and <c>cat-file</c>, which do not
/// modify the repository or apply filters (no network access through LFS).
/// <para>Two baselines:</para>
/// <list type="bullet">
///   <item><see cref="BaselineMode.Session"/>: what has changed since this instance was created (or reset). It compares
///     against the commit that existed then, so the agent's later commits do not hide its changes; files
///     that were already uncommitted are copied in memory and only appear if they change again.</item>
///   <item><see cref="BaselineMode.Head"/>: what differs from the current commit.</item>
/// </list>
/// </summary>
public sealed class GitChangeSource : IChangeSource
{
    public enum BaselineMode { Session, Head }

    internal const long MaxFileBytes = 2L * 1024 * 1024;
    internal const long MaxSnapshotBytes = 50L * 1024 * 1024;

    /// <summary>State of a file that was already uncommitted at the start. <c>Content</c> is null if it was deleted.</summary>
    private sealed record Snapshot(byte[]? Content, bool Deleted, bool TooLarge, long Size, long ModifiedMillis);

    private sealed record Baseline(string Sha, IReadOnlyDictionary<FilePath, Snapshot> Dirty);

    private static readonly IReadOnlyDictionary<FilePath, Snapshot> NoSnapshots = new Dictionary<FilePath, Snapshot>();

    private readonly GitRepository _repo;
    private readonly GitCli _git;
    private readonly FilePath _projectDir;
    private volatile Baseline _baseline;

    public GitChangeSource(GitRepository repo, FilePath projectDir, BaselineMode mode)
    {
        _repo = repo;
        _git = repo.Git;
        _projectDir = projectDir;
        Mode = mode;
        _baseline = TakeBaseline();
    }

    public BaselineMode Mode { get; }

    public void ResetBaseline() => _baseline = TakeBaseline();

    public IReadOnlyList<FileChange> Changes()
    {
        Baseline baseline = CurrentBaseline();
        List<FileChange> changes = [];
        foreach (PorcelainParser.Entry entry in Entries(baseline.Sha))
        {
            FilePath path = _repo.Root.Combine(entry.Path);
            if (!path.IsUnder(_projectDir)) continue;
            bool exists = Exists(path);
            ChangeKind kind;
            if (baseline.Dirty.TryGetValue(path, out Snapshot? before))
            {
                if (!exists && before.Deleted) continue;
                if (exists && !before.Deleted && SameAsSnapshot(path, before)) continue;
                kind = !exists ? ChangeKind.Deleted : before.Deleted ? ChangeKind.Created : ChangeKind.Modified;
            }
            else if (!exists)
            {
                kind = ChangeKind.Deleted;
            }
            else
            {
                kind = entry.Status is 'A' or '?' ? ChangeKind.Created : ChangeKind.Modified;
            }
            changes.Add(new FileChange(path, kind, exists ? Modified(path) : null));
        }
        changes.Sort((a, b) => a.Path.CompareTo(b.Path));
        return changes;
    }

    public FileVersions Versions(FilePath path)
    {
        if (Directory.Exists(path.Value)) return FileVersions.Unavailable(path, UnavailableReason.SubmoduleOrFolder);
        Baseline baseline = CurrentBaseline();
        byte[]? before;
        if (baseline.Dirty.TryGetValue(path, out Snapshot? snapshot))
        {
            if (snapshot.TooLarge) return FileVersions.Unavailable(path, UnavailableReason.NoBaseline);
            before = snapshot.Content;
        }
        else
        {
            Blob? blob = ReadBlob(baseline.Sha, path);
            if (blob is { TooLarge: true }) return FileVersions.Unavailable(path, UnavailableReason.TooLarge);
            before = blob?.Content;
        }
        byte[]? after = null;
        if (File.Exists(path.Value))
        {
            if (new FileInfo(path.Value).Length > MaxFileBytes) return FileVersions.Unavailable(path, UnavailableReason.TooLarge);
            after = File.ReadAllBytes(path.Value);
        }
        if ((before is not null && UnifiedDiffEngine.LooksBinary(before)) || (after is not null && UnifiedDiffEngine.LooksBinary(after)))
        {
            return FileVersions.Unavailable(path, UnavailableReason.Binary);
        }
        return new FileVersions(path, Text(before), Text(after), null);
    }

    public void Dispose()
    {
    }

    // ---- baseline ----

    private Baseline CurrentBaseline() =>
        // In HEAD mode the reference is the current commit, not the one from when it was created
        Mode == BaselineMode.Head ? new Baseline(HeadOrEmptyTree(), NoSnapshots) : _baseline;

    private Baseline TakeBaseline()
    {
        string sha = HeadOrEmptyTree();
        if (Mode == BaselineMode.Head) return new Baseline(sha, NoSnapshots);
        Dictionary<FilePath, Snapshot> dirty = [];
        long total = 0;
        foreach (PorcelainParser.Entry entry in Entries(sha))
        {
            FilePath path = _repo.Root.Combine(entry.Path);
            if (!path.IsUnder(_projectDir)) continue;
            if (!Exists(path))
            {
                dirty[path] = new Snapshot(null, true, false, 0, 0);
            }
            else if (File.Exists(path.Value))
            {
                long size = new FileInfo(path.Value).Length;
                long modified = ModifiedMillis(path);
                if (size > MaxFileBytes || total + size > MaxSnapshotBytes)
                {
                    dirty[path] = new Snapshot(null, false, true, size, modified);
                }
                else
                {
                    dirty[path] = new Snapshot(File.ReadAllBytes(path.Value), false, false, size, modified);
                    total += size;
                }
            }
        }
        return new Baseline(sha, dirty);
    }

    private string HeadOrEmptyTree()
    {
        string? head = _repo.HeadSha();
        if (head is not null) return head;
        // Repository without commits: the reference is the empty tree (using the repository's own hash algorithm)
        return _git.Run("hash-object", "-t", "tree", "--stdin").Text.Trim();
    }

    private static bool SameAsSnapshot(FilePath path, Snapshot snapshot)
    {
        // A file that is now a folder is no longer the same one
        if (!File.Exists(path.Value)) return false;
        long size = new FileInfo(path.Value).Length;
        if (snapshot.TooLarge) return size == snapshot.Size && ModifiedMillis(path) == snapshot.ModifiedMillis;
        return size == snapshot.Size && File.ReadAllBytes(path.Value).AsSpan().SequenceEqual(snapshot.Content!);
    }

    // ---- git queries ----

    private List<PorcelainParser.Entry> Entries(string baseSha)
    {
        string scope = _repo.Pathspec(_projectDir);
        List<PorcelainParser.Entry> entries = [];
        entries.AddRange(PorcelainParser.NameStatus(
            _git.Run("diff", "--no-renames", "--name-status", "-z", baseSha, "--", scope).Stdout));
        foreach (string path in PorcelainParser.Paths(
                     _git.Run("ls-files", "--others", "--exclude-standard", "-z", "--", scope).Stdout))
        {
            entries.Add(new PorcelainParser.Entry('?', path));
        }
        return entries;
    }

    private sealed record Blob(byte[]? Content, bool TooLarge);

    /// <summary>Content of the file at a commit, without filters; null if it did not exist there. A large file is not read.</summary>
    private Blob? ReadBlob(string sha, FilePath path)
    {
        string spec = sha + ":" + path.RelativeTo(_repo.Root).Replace('\\', '/');
        GitCli.Result size = _git.Exec("cat-file", "-s", spec);
        if (size.ExitCode != 0) return null;
        if (long.Parse(size.Text.Trim()) > MaxFileBytes) return new Blob(null, true);
        return new Blob(_git.Run("cat-file", "blob", spec).Stdout, false);
    }

    private static bool Exists(FilePath path) => File.Exists(path.Value) || Directory.Exists(path.Value);

    private static DateTimeOffset Modified(FilePath path) => new(File.GetLastWriteTimeUtc(path.Value), TimeSpan.Zero);

    private static long ModifiedMillis(FilePath path) => Modified(path).ToUnixTimeMilliseconds();

    private static string? Text(byte[]? bytes) => bytes is null ? null : Encoding.UTF8.GetString(bytes);
}
