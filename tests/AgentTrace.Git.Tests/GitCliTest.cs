using AgentTrace.Core;

namespace AgentTrace.Git.Tests;

// No Java equivalent: the timeout had no test of its own.
public sealed class GitCliTest : GitTestBase
{
    [Fact]
    public void ExceedingTheTimeoutKillsGitAndThrowsAnIOException()
    {
        // A zero timeout raced with a fast git; a git alias that sleeps (git runs it with its own shell, also on Windows) cannot finish in time
        GitCli impatient = new("git", Tmp, TimeSpan.FromMilliseconds(300));
        IOException e = Assert.Throws<IOException>(() => impatient.Exec("-c", "alias.slow=!sleep 30", "slow"));
        Assert.Contains("timed out", e.Message);
    }
}
