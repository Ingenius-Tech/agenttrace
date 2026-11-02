namespace AgentTrace.Git;

/// <summary>A failed git call, with its exit code and git's error message. The message is an English diagnostic.</summary>
public sealed class GitException(string command, int exitCode, string stderr)
    : IOException(Describe(command, exitCode, stderr))
{
    public int ExitCode { get; } = exitCode;

    public string Stderr { get; } = stderr;

    /// <summary>Git refuses to work in a folder owned by another user (<c>safe.directory</c>). Presentation shows the hint.</summary>
    public bool IsDubiousOwnership => Stderr.Contains("dubious ownership", StringComparison.Ordinal);

    /// <summary>The folder is not inside any repository.</summary>
    public bool IsNotARepository => Stderr.Contains("not a git repository", StringComparison.Ordinal);

    private static string Describe(string command, int exitCode, string stderr)
    {
        string detail = string.IsNullOrWhiteSpace(stderr) ? "no message" : stderr.Trim();
        return "git " + command + " exited with code " + exitCode + ": " + detail;
    }
}
