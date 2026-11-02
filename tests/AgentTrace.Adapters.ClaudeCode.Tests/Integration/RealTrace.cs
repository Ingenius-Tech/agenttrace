using AgentTrace.Core;
using AgentTrace.Testing;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// Source of the REAL Claude Code trace for the acceptance tests. The trace is not copied into the repository.
/// Source: the <c>AGENTTRACE_REAL_TRACE</c> variable (a file); if unset, the most recent session of this same project
/// in <c>~/.claude/projects</c>. The tests are skipped if there is none.
/// </summary>
internal static class RealTrace
{
    public sealed record Found(ClaudeCodeAdapter Adapter, FilePath Session, bool OwnProject, FilePath ProjectRoot);

    public static Found LocateOrSkip()
    {
        string? explicitFile = Environment.GetEnvironmentVariable("AGENTTRACE_REAL_TRACE");
        var adapter = new ClaudeCodeAdapter();
        FilePath root = TestPaths.RepoRoot();
        FilePath session;
        bool own = string.IsNullOrWhiteSpace(explicitFile);
        if (!own)
        {
            session = new FilePath(explicitFile!);
        }
        else
        {
            IReadOnlyList<FilePath> sessions = adapter.FindTraceFiles(root);
            Assert.SkipWhen(sessions.Count == 0, "No real Claude Code traces for this project: test skipped");
            session = sessions[0];
        }
        Assert.SkipUnless(File.Exists(session.Value), "The trace does not exist: " + session);
        return new Found(adapter, session, own, root);
    }

}
