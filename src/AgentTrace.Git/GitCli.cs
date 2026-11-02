using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AgentTrace.Core;

namespace AgentTrace.Git;

/// <summary>
/// Runs the system <c>git</c> program. Meant for observing: no optional locks on the index,
/// no interactive prompts, no network and unescaped paths. Thread-safe.
/// </summary>
public sealed class GitCli(string executable, FilePath workDir, TimeSpan timeout)
{
    public sealed record Result(int ExitCode, byte[] Stdout, string Stderr)
    {
        public string Text => Encoding.UTF8.GetString(Stdout);
    }

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public FilePath WorkDir { get; } = workDir;

    /// <summary>Uses the <c>AGENTTRACE_GIT=&lt;path&gt;</c> environment variable if present, otherwise <c>git</c> from the PATH.</summary>
    public static GitCli ForDirectory(FilePath workDir)
    {
        string? configured = Environment.GetEnvironmentVariable("AGENTTRACE_GIT");
        return new GitCli(string.IsNullOrEmpty(configured) ? "git" : configured, workDir, DefaultTimeout);
    }

    public GitCli InDirectory(FilePath dir) => new(executable, dir, timeout);

    /// <summary>Runs git and returns its output; throws <see cref="GitException"/> if it ends with an error.</summary>
    public Result Run(params string[] args)
    {
        Result result = Exec(args);
        if (result.ExitCode != 0) throw new GitException(string.Join(" ", args), result.ExitCode, result.Stderr);
        return result;
    }

    /// <summary>Runs git and returns the result whatever the exit code.</summary>
    public Result Exec(params string[] args) => ExecAsync(args).GetAwaiter().GetResult();

    /// <summary>
    /// Like <see cref="Exec"/>, without blocking the thread. If git exceeds the time limit the process (and its children) is killed and
    /// <see cref="IOException"/> is thrown; if <paramref name="cancellationToken"/> is cancelled, it is killed and the cancellation propagates.
    /// </summary>
    public async Task<Result> ExecAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo info = new(executable)
        {
            WorkingDirectory = WorkDir.Value,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("--no-optional-locks");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.quotepath=false");
        foreach (string arg in args) info.ArgumentList.Add(arg);
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["LC_ALL"] = "C";

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new IOException("could not start " + executable);
        }
        catch (Win32Exception e)
        {
            // Missing program or invalid working directory: the rest of the code only knows IOException
            throw new IOException("could not run " + executable + ": " + e.Message, e);
        }

        using (process)
        {
            process.StandardInput.Close();
            // stdout and stderr are read at the same time: otherwise a process with a lot of output blocks on a full buffer
            Task<byte[]> stdout = ReadAllAsync(process.StandardOutput.BaseStream);
            Task<byte[]> stderr = ReadAllAsync(process.StandardError.BaseStream);
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                if (cancellationToken.IsCancellationRequested) throw;
                throw new IOException("git " + string.Join(" ", args) + " timed out after " + (long)timeout.TotalSeconds + " s");
            }
            byte[] outBytes = await stdout.ConfigureAwait(false);
            byte[] errBytes = await stderr.ConfigureAwait(false);
            return new Result(process.ExitCode, outBytes, Encoding.UTF8.GetString(errBytes));
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // it had already exited
        }
        catch (Win32Exception)
        {
            // could not be killed: the process is abandoned
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        try
        {
            using MemoryStream buffer = new();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            return buffer.ToArray();
        }
        catch (IOException)
        {
            return [];
        }
    }
}
