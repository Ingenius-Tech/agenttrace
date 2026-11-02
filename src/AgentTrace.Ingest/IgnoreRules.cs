using AgentTrace.Core;

namespace AgentTrace.Ingest;

/// <summary>
/// Which files and folders of the project are neither shown nor watched. <c>.git</c> always. With git, whatever git ignores
/// (which the user has already decided is noise); without git, a fixed list of usual folders.
/// </summary>
public sealed class IgnoreRules
{
    /// <summary>Folders that are skipped when there is no git to say what is ignored.</summary>
    public static IReadOnlySet<string> FallbackDirectories { get; } =
        new HashSet<string>([".git", "node_modules", "target", ".venv"], StringComparer.Ordinal);

    private readonly FilePath _root;
    private readonly HashSet<FilePath> _ignoredByGit;
    private readonly bool _useFallback;

    private IgnoreRules(FilePath root, HashSet<FilePath> ignoredByGit, bool useFallback)
    {
        _root = root;
        _ignoredByGit = ignoredByGit;
        _useFallback = useFallback;
    }

    /// <param name="projectDir">Project root.</param>
    /// <param name="ignoredByGit">Paths that git ignores (an ignored folder includes everything it contains).</param>
    public static IgnoreRules WithGit(FilePath projectDir, IEnumerable<FilePath> ignoredByGit) =>
        new(projectDir, [.. ignoredByGit], false);

    public static IgnoreRules WithoutGit(FilePath projectDir) => new(projectDir, [], true);

    public bool IsIgnored(FilePath path)
    {
        if (path == _root) return false;
        for (FilePath? p = path; p is { } current && current.IsUnder(_root) && current != _root; p = current.Parent)
        {
            string name = current.FileName;
            if (name.Length == 0) continue;
            if (name == ".git") return true;
            if (_useFallback && FallbackDirectories.Contains(name)) return true;
            if (_ignoredByGit.Contains(current)) return true;
        }
        return false;
    }
}
