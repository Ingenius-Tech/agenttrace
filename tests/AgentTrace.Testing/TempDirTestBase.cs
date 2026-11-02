using AgentTrace.Core;

namespace AgentTrace.Testing;

/// <summary>A temporary folder per test (JUnit's <c>@TempDir</c>).</summary>
public abstract class TempDirTestBase : IDisposable
{
    protected FilePath Dir { get; }

    protected TempDirTestBase()
    {
        string path = Path.Combine(Path.GetTempPath(), "agenttrace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        Dir = new FilePath(path);
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Dir.Value, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Dir.Value, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A watcher or a reader may hold the folder open for a moment; that is not a test failure
        }
        GC.SuppressFinalize(this);
    }
}
