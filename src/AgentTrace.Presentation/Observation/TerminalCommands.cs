namespace AgentTrace.Presentation.Observation;

/// <summary>Which executable to start (and with which arguments) to open a system terminal. Whoever starts it sets the working directory.</summary>
public sealed record TerminalCommand(string File, IReadOnlyList<string> Args);

/// <summary>
/// Chooses the native terminal with which to open a project's folder (plan 005, option A: a channel to terminal agents,
/// which write their traces and <c>TraceFollower</c> follows them without a new integration). Pure: it starts nothing and does not inspect the system except through <c>onPath</c>.
/// </summary>
public static class TerminalCommands
{
    public const string PreferenceVariable = "AGENTTRACE_TERMINAL";

    /// <param name="onPath">Whether an executable exists on the PATH.</param>
    /// <param name="preferred">Terminal chosen by the user (<c>AGENTTRACE_TERMINAL</c>): started without arguments in the project folder.</param>
    /// <returns>Null if there is no known terminal.</returns>
    public static TerminalCommand? For(string directory, bool windows, bool macOs, Func<string, bool> onPath, string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) return new TerminalCommand(preferred.Trim(), []);
        if (windows) return onPath("wt.exe") ? new TerminalCommand("wt.exe", ["-d", directory]) : new TerminalCommand("cmd.exe", []);
        if (macOs) return new TerminalCommand("open", ["-a", "Terminal", directory]);
        // Linux: the first one that exists; those that do not accept a folder inherit the working one
        (string Name, string[] Args)[] candidates =
        [
            ("x-terminal-emulator", []),
            ("gnome-terminal", ["--working-directory=" + directory]),
            ("konsole", ["--workdir", directory]),
            ("xfce4-terminal", ["--working-directory=" + directory]),
            ("xterm", []),
        ];
        foreach ((string name, string[] args) in candidates)
        {
            if (onPath(name)) return new TerminalCommand(name, args);
        }
        return null;
    }
}
