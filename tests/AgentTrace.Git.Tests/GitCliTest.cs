using AgentTrace.Core;

namespace AgentTrace.Git.Tests;

// No Java equivalent: the timeout had no test of its own.
public sealed class GitCliTest : GitTestBase
{
    [Fact]
    public void ExceedingTheTimeoutKillsGitAndThrowsAnIOException()
    {
        GitCli instant = new("git", Tmp, TimeSpan.Zero);
        IOException e = Assert.Throws<IOException>(() => instant.Exec("--version"));
        Assert.Contains("timed out", e.Message);
    }
}
