using System.Diagnostics;
using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Desktop;

/// <summary>Opens the system's native terminal in a project's folder. Returns the reason if it cannot (does not throw).</summary>
public static class TerminalLauncher
{
    public static string? Open(FilePath directory)
    {
        TerminalCommand? command = TerminalCommands.For(directory.Value, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OnPath,
            Environment.GetEnvironmentVariable(TerminalCommands.PreferenceVariable));
        if (command is null) return string.Format(CultureInfo.CurrentCulture, ShellStrings.TerminalNotFoundFormat, TerminalCommands.PreferenceVariable);
        try
        {
            // UseShellExecute: on Windows, this is how cmd.exe opens its own console window in the folder
            var info = new ProcessStartInfo(command.File) { WorkingDirectory = directory.Value, UseShellExecute = true };
            foreach (string arg in command.Args) info.ArgumentList.Add(arg);
            Process.Start(info)?.Dispose();
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return string.Format(CultureInfo.CurrentCulture, ShellStrings.TerminalOpenFailedFormat, command.File, e.Message);
        }
    }

    private static bool OnPath(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, executable)));
}
