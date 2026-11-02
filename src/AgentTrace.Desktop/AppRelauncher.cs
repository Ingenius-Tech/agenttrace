using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Desktop;

/// <summary>Starts a new instance of this application (to apply a language change). Returns the reason if it cannot; never throws.</summary>
public static class AppRelauncher
{
    public static string? Relaunch(FilePath? project)
    {
        string? executable = Environment.ProcessPath;
        if (executable is null) return ShellStrings.RestartUnknownExecutable;
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false };
            if (project is { } p) info.ArgumentList.Add(p.Value);
            Process.Start(info)?.Dispose();
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return e.Message;
        }
    }
}
