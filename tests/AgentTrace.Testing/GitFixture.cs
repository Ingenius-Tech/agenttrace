using System.Text;
using AgentTrace.Core;
using AgentTrace.Git;

namespace AgentTrace.Testing;

/// <summary>Temporary, real git repositories for tests.</summary>
public sealed class GitFixture
{
    private GitFixture(FilePath dir)
    {
        Dir = dir;
        Git = GitCli.ForDirectory(dir);
    }

    public FilePath Dir { get; }

    public GitCli Git { get; }

    public static bool GitAvailable() => GitRepository.IsAvailable(GitCli.ForDirectory(new FilePath(".")));

    /// <summary>Creates a repository with identity and line endings fixed, so it does not depend on the user's configuration.</summary>
    public static GitFixture Init(FilePath dir)
    {
        Directory.CreateDirectory(dir.Value);
        GitFixture f = new(dir);
        f.Git.Run("init");
        f.Git.Run("config", "user.name", "Test");
        f.Git.Run("config", "user.email", "test@example.com");
        f.Git.Run("config", "core.autocrlf", "false");
        f.Git.Run("config", "commit.gpgsign", "false");
        return f;
    }

    public FilePath Write(string relative, string content) => WriteBytes(relative, new UTF8Encoding(false).GetBytes(content));

    public FilePath WriteBytes(string relative, byte[] content)
    {
        FilePath file = Dir.Combine(relative);
        Directory.CreateDirectory(file.Parent!.Value.Value);
        File.WriteAllBytes(file.Value, content);
        return file;
    }

    public void SetModified(FilePath file, long epochMillis) =>
        File.SetLastWriteTimeUtc(file.Value, DateTimeOffset.FromUnixTimeMilliseconds(epochMillis).UtcDateTime);

    public string CommitAll(string message)
    {
        Git.Run("add", "-A");
        Git.Run("commit", "-m", message);
        return Git.Run("rev-parse", "HEAD").Text.Trim();
    }

    public void Run(params string[] args) => Git.Run(args);
}
