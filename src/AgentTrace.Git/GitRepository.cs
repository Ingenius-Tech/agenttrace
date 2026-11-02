using AgentTrace.Core;

namespace AgentTrace.Git;

/// <summary>A local git repository: its root, its data directory and the current commit.</summary>
public sealed class GitRepository
{
    private GitRepository(GitCli git, FilePath root, FilePath gitDir)
    {
        Git = git;
        Root = root;
        GitDir = gitDir;
    }

    public GitCli Git { get; }

    public FilePath Root { get; }

    public FilePath GitDir { get; }

    /// <summary>Checks that the git program exists and responds.</summary>
    public static bool IsAvailable(GitCli cli)
    {
        try
        {
            return cli.Exec("--version").ExitCode == 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the repository that contains the folder (it can be a subdirectory or a worktree).
    /// Returns null if it is not inside any; throws if git fails for another reason (for example, dubious ownership).
    /// </summary>
    public static GitRepository? Locate(GitCli cli, FilePath projectDir)
    {
        GitCli inProject = cli.InDirectory(projectDir);
        GitCli.Result result = inProject.Exec("rev-parse", "--show-toplevel", "--absolute-git-dir");
        if (result.ExitCode != 0)
        {
            GitException error = new("rev-parse", result.ExitCode, result.Stderr);
            if (error.IsNotARepository) return null;
            throw error;
        }
        string[] lines = result.Text.Trim().ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 2) return null;
        FilePath root = new(lines[0]);
        return new GitRepository(cli.InDirectory(root), root, new FilePath(lines[1]));
    }

    /// <summary>
    /// Creates a repository in the folder. With <paramref name="initialCommit"/>, adds everything present and makes a first commit
    /// (needs git to have <c>user.name</c> and <c>user.email</c>; otherwise throws with git's message).
    /// </summary>
    public static void Init(GitCli cli, FilePath dir, bool initialCommit)
    {
        GitCli inDir = cli.InDirectory(dir);
        inDir.Run("init");
        if (initialCommit)
        {
            inDir.Run("add", "-A");
            inDir.Run("commit", "-m", "Initial commit (AgentTrace Studio)");
        }
    }

    /// <summary>Project folder relative to the repository root ("." if it is the root), so that git does not walk the rest.</summary>
    public string Pathspec(FilePath projectDir)
    {
        string spec = projectDir.RelativeTo(Root).Replace('\\', '/');
        return spec.Length == 0 ? "." : spec;
    }

    /// <summary>
    /// Files and folders of the project that git ignores (absolute paths). An ignored folder is returned whole,
    /// without listing what it contains. Does not include what is already tracked.
    /// </summary>
    public IReadOnlySet<FilePath> IgnoredPaths(FilePath projectDir)
    {
        GitCli.Result result = Git.Run("ls-files", "--others", "--ignored", "--exclude-standard", "--directory", "-z", "--",
            Pathspec(projectDir));
        HashSet<FilePath> ignored = [];
        foreach (string relative in PorcelainParser.Paths(result.Stdout))
        {
            ignored.Add(Root.Combine(relative));
        }
        return ignored;
    }

    /// <summary>SHA of the current commit, or null if the repository has no commits yet.</summary>
    public string? HeadSha()
    {
        GitCli.Result result = Git.Exec("rev-parse", "--verify", "--quiet", "HEAD");
        return result.ExitCode == 0 ? result.Text.Trim() : null;
    }
}
