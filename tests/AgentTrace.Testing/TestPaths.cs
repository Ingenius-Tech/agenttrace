using AgentTrace.Core;

namespace AgentTrace.Testing;

public static class TestPaths
{
    /// <summary>The root of this repository (the <c>user.dir</c> of the Java tests): climbs from the binary up to the folder with <c>.git</c>.</summary>
    public static FilePath RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return new FilePath(dir.FullName);
            }
        }
        return new FilePath(Directory.GetCurrentDirectory());
    }
}
