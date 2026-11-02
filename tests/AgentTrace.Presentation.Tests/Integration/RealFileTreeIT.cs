using AgentTrace.Core;
using AgentTrace.Presentation.Text;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Integration;

/// <summary>
/// Acceptance with REAL changes and reads by Claude Code in this very project: real git and the real trace of the session.
/// It is enabled with <c>AGENTTRACE_ACCEPTANCE_TREE=&lt;file&gt;=&lt;STATE&gt;[,...]</c> with STATE in CREATED, MODIFIED, DELETED or READ
/// (files relative to the project, touched shortly before with the corresponding tool). Without the variable, it is skipped.
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class RealFileTreeIT
{
    [Fact]
    public void RealChangesAndReadsShowUpInTheTreeModel()
    {
        (RealAcceptance a, string spec) = RealAcceptance.StartOrSkip("AGENTTRACE_ACCEPTANCE_TREE");
        using (a)
        {
            foreach (string item in spec.Split(','))
            {
                string[] parts = item.Trim().Split('=');
                FilePath file = a.Project.Combine(parts[0]);
                string expected = parts[1];
                bool reached = false;
                try
                {
                    Eventually.Until(parts[0] + " reached " + expected, () => Reached(a, file, expected), 25_000, 50);
                    reached = true;
                }
                catch (Xunit.Sdk.XunitException)
                {
                    // reported below with the detail
                }
                var index = FileStatusIndex.Of(a.Project, a.Latest(), a.Observer.Reads());
                var rules = a.Observer.IgnoreRules();
                FileNode? node = DirectoryLister.Children(file.Parent!.Value, rules, index).FirstOrDefault(n => n.Path == file);
                NodeDecoration decoration = node is null ? NodeDecoration.None : NodeDecoration.Of(node, index);
                Console.WriteLine("[REAL-TREE] " + parts[0] + " (expected " + expected + ") → "
                    + (node is null ? "NOT IN THE TREE" : (node.Ghost ? "ghost · " : "") + decoration.Tooltip
                        + " · badge '" + decoration.Badge + "'" + (decoration.Read ? " · eye" : "")));
                Assert.True(reached, parts[0] + " did not reach the expected state " + expected + ": " + index.File(file));
                Assert.NotNull(node);

                switch (expected)
                {
                    case "CREATED" or "MODIFIED":
                        Assert.Equal(Enum.Parse<ChangeKind>(expected, true), decoration.Kind);
                        Assert.Equal(Actor.Agent, decoration.Actor); // the agent did it: it must be attributed to it
                        Assert.False(decoration.Strike);
                        break;
                    case "DELETED":
                        Assert.Equal(ChangeKind.Deleted, decoration.Kind);
                        Assert.True(node.Ghost, "a deleted file must look like a ghost");
                        Assert.True(decoration.Strike);
                        break;
                    case "READ":
                        Assert.True(decoration.Read, "it must have the read eye");
                        break;
                    default:
                        Assert.Fail("Unknown state: " + expected);
                        break;
                }
            }
        }
    }

    private static bool Reached(RealAcceptance a, FilePath file, string expected)
    {
        var index = FileStatusIndex.Of(a.Project, a.Latest(), a.Observer.Reads());
        if (index.File(file) is not { } status) return false;
        return expected switch
        {
            "CREATED" or "MODIFIED" => status.Kind == Enum.Parse<ChangeKind>(expected, true) && status.Actor == Actor.Agent,
            "DELETED" => status.Kind == ChangeKind.Deleted,
            "READ" => status.Read,
            _ => false,
        };
    }
}
