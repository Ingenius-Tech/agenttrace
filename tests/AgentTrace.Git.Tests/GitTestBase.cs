using AgentTrace.Core;
using AgentTrace.Testing;

namespace AgentTrace.Git.Tests;

/// <summary>
/// Temporary folder per test (equivalent to JUnit's <c>@TempDir</c>) and skip if git is not installed
/// (equivalent to <c>assumeTrue(gitAvailable)</c>).
/// </summary>
public abstract class GitTestBase : IDisposable
{
    protected GitTestBase()
    {
        Assert.SkipUnless(GitFixture.GitAvailable(), "These tests need git installed");
        Tmp = new FilePath(Path.Combine(Path.GetTempPath(), "agenttrace-git-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Tmp.Value);
    }

    protected FilePath Tmp { get; }

    public void Dispose()
    {
        try
        {
            // Git objects are read-only: the attribute must be removed to be able to delete them
            foreach (string file in Directory.EnumerateFiles(Tmp.Value, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Tmp.Value, recursive: true);
        }
        catch (IOException)
        {
            // a temporary directory that cannot be deleted does not invalidate the test
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
