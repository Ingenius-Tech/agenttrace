using AgentTrace.Core;

namespace AgentTrace.Testing;

public static class TestPaths
{
    /// <summary>
    /// The system temporary folder with symbolic links resolved. On macOS <c>/var</c> is a link to <c>/private/var</c> and git
    /// reports the real path, so tests that compare paths with git's output must start from the real one.
    /// </summary>
    public static string TempRoot()
    {
        string temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal)) temp = "/private" + temp;
        return temp;
    }

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
