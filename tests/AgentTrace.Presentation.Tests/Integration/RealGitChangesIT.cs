using AgentTrace.Core;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Integration;

/// <summary>
/// Acceptance with REAL changes made by Claude Code in this very project: real git and the real trace of the session.
/// It is enabled with <c>AGENTTRACE_ACCEPTANCE_FILES=&lt;file&gt;=&lt;Tool&gt;[,...]</c>, where each file (relative to the
/// project) was modified by the agent with that tool shortly before launching the test. Without the variable, it is skipped.
/// Example: <c>AGENTTRACE_ACCEPTANCE_FILES=acceptance-bash.tmp=Bash,acceptance-write.tmp=Write</c>
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class RealGitChangesIT
{
    [Fact]
    public void RealChangesMadeByClaudeCodeAreAttributedToTheirToolCalls()
    {
        (RealAcceptance a, string spec) = RealAcceptance.StartOrSkip("AGENTTRACE_ACCEPTANCE_FILES");
        using (a)
        {
            foreach (string item in spec.Split(','))
            {
                string[] parts = item.Trim().Split('=');
                FilePath file = a.Project.Combine(parts[0]);
                string tool = parts[1];
                bool attributed = false;
                try
                {
                    Eventually.Until(parts[0] + " attributed", () =>
                        a.Find(file) is { } e && e.Attribution.Confidence != Confidence.None
                        && e.Attribution.Calls.Any(c => c.ToolName == tool), 20_000, 50);
                    attributed = true;
                }
                catch (Xunit.Sdk.XunitException)
                {
                    // reported below with the details
                }
                ChangeEntry? entry = a.Find(file);
                Console.WriteLine("[REAL-GIT] " + parts[0] + " → " + (entry is null
                    ? "not in the list"
                    : entry.Change.Kind + " · " + entry.Attribution.Confidence + " · [" + string.Join(", ", entry.Attribution.Calls.Select(c => c.ToolName)) + "]"));
                Assert.True(attributed, parts[0] + " was not attributed to a " + tool + " call" + ": " + entry);
                if (tool == "Write")
                {
                    Assert.True(entry!.Attribution.Confidence == Confidence.Exact,
                        "a Write with a result in the trace must be exact: " + entry.Attribution.Confidence);
                }
                // And its real content can be seen
                Assert.True(a.Observer.Versions(file).UnavailableReason is null, "no content for " + parts[0]);
            }
        }
    }
}
