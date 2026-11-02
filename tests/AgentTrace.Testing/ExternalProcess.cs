using System.Diagnostics;
using Xunit;

namespace AgentTrace.Testing;

/// <summary>
/// Makes changes on disk from ANOTHER process, as any tool outside the application would (the equivalent of the
/// external JVMs of the Java tests). Uses the system shell: PowerShell on Windows, <c>sh</c> elsewhere.
/// The texts written must be ASCII: command-line arguments do not guarantee any other encoding.
/// </summary>
public static class ExternalProcess
{
    /// <summary>Writes <paramref name="text"/> as UTF-8 without BOM, creating any missing folders.</summary>
    public static void Write(string path, string text) =>
        Script(
            "$p = '" + Escape(path) + "'; [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p)) | Out-Null; "
            + "[IO.File]::WriteAllText($p, '" + Escape(text) + "', (New-Object Text.UTF8Encoding $false))",
            "mkdir -p \"$(dirname '" + Escape(path) + "')\" && printf '%s' '" + Escape(text) + "' > '" + Escape(path) + "'");

    public static void Delete(string path) =>
        Script("Remove-Item -LiteralPath '" + Escape(path) + "'", "rm -- '" + Escape(path) + "'");

    /// <summary>
    /// Starts a process that rewrites the file <paramref name="count"/> times ("version N" and a line with Spanish accents) with a pause
    /// of <paramref name="pauseMillis"/> ms between writes. Whoever starts it must wait for it or call <see cref="Process.Kill()"/>.
    /// </summary>
    public static Process StartRewriter(string path, int count, int pauseMillis)
    {
        string script = OperatingSystem.IsWindows()
            ? "$p = '" + Escape(path) + "'; for ($i = 1; $i -le " + count + "; $i++) { "
              + "$t = 'version ' + $i + [char]10 + 'l' + [char]0xED + 'nea con ' + [char]0xF1 + [char]10; "   // lang-ok: Spanish accents fixture
              + "[IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding $false)); Start-Sleep -Milliseconds " + pauseMillis + " }"
            : "i=1; while [ $i -le " + count + " ]; do printf 'version %s\\nl\\303\\255nea con \\303\\261\\n' $i > '" + Escape(path)   // lang-ok: Spanish accents fixture
              + "'; sleep 0." + pauseMillis.ToString("000") + "; i=$((i+1)); done";
        return Start(script);
    }

    private static void Script(string windows, string unix)
    {
        using Process process = Start(OperatingSystem.IsWindows() ? windows : unix);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "the external process did not finish");
        Assert.True(process.ExitCode == 0, output);
    }

    private static Process Start(string script)
    {
        ProcessStartInfo info = OperatingSystem.IsWindows()
            ? new("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script])
            : new("sh", ["-c", script]);
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        return Process.Start(info)!;
    }

    private static string Escape(string text) => text.Replace("'", "''");
}
