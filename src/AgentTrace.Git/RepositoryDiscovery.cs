using AgentTrace.Core;

namespace AgentTrace.Git;

/// <summary>The repositories found under a folder and the candidates git rejected (with the reason).</summary>
public sealed record DiscoveredRepositories(IReadOnlyList<GitRepository> Repositories, IReadOnlyList<string> Problems);

/// <summary>
/// Looks for git repositories in the subfolders of a folder that is not one (a project made of several repositories).
/// Breadth-first, through the file system: a subfolder with a <c>.git</c> entry is a repository and is not descended
/// into; git is only run on the candidates. Read-only.
/// </summary>
public static class RepositoryDiscovery
{
    /// <summary>Subfolder levels examined: 3 covers <c>root/group/repo</c>.</summary>
    public const int MaxDepth = 3;

    public const int MaxRepositories = 100;

    private static readonly HashSet<string> Skipped = new(["node_modules", "target", ".venv", "bin", "obj"], StringComparer.Ordinal);

    public static DiscoveredRepositories Find(GitCli cli, FilePath root)
    {
        List<GitRepository> found = [];
        List<string> problems = [];
        HashSet<FilePath> seen = [];
        Queue<(string Dir, int Depth)> pending = new();
        pending.Enqueue((root.Value, 0));
        while (pending.Count > 0 && found.Count < MaxRepositories)
        {
            (string dir, int depth) = pending.Dequeue();
            foreach (string child in Children(dir))
            {
                if (found.Count >= MaxRepositories) break;
                if (HasGitEntry(child))
                {
                    TryAdd(cli, new FilePath(child), found, problems, seen);
                }
                else if (depth + 1 < MaxDepth)
                {
                    pending.Enqueue((child, depth + 1));
                }
            }
        }
        found.Sort((a, b) => a.Root.CompareTo(b.Root));
        return new DiscoveredRepositories(found, problems);
    }

    private static void TryAdd(GitCli cli, FilePath dir, List<GitRepository> found, List<string> problems, HashSet<FilePath> seen)
    {
        try
        {
            GitRepository? repo = GitRepository.Locate(cli, dir);
            if (repo is not null && seen.Add(repo.Root)) found.Add(repo);
        }
        catch (IOException e)
        {
            problems.Add(dir.FileName + ": " + e.Message);
        }
    }

    /// <summary>Real subfolders (no links or junctions, which can form loops), excluding those that never contain projects.</summary>
    private static IEnumerable<string> Children(string dir)
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(dir).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (string child in children.OrderBy(c => c, StringComparer.Ordinal))
        {
            if (Skipped.Contains(Path.GetFileName(child))) continue;
            if (IsLink(child)) continue;
            yield return child;
        }
    }

    private static bool IsLink(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary><c>.git</c> is a folder in a normal repository and a file in a worktree or submodule.</summary>
    private static bool HasGitEntry(string dir)
    {
        string git = Path.Combine(dir, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }
}
