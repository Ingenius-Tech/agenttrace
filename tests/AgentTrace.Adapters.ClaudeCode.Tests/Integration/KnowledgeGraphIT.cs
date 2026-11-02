using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Knowledge;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// e2e of the knowledge graph without UI: Claude Code trace → adapter → <c>KnowledgeBuilder</c> → layout.
/// The edges are checked against an independent calculation made here, by hand, over the same events.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KnowledgeGraphIT : TempDirTest
{
    private const string Session = "aaaaaaaa-0000-0000-0000-000000000001";

    private static KnowledgeGraph Build(IReadOnlyList<AgentEvent> events, FilePath? root)
    {
        var b = new KnowledgeBuilder(root);
        foreach (AgentEvent e in events) b.Accept(e);
        return b.Snapshot();
    }

    private static string Key(string kind, FilePath from, FilePath to) => kind + "|" + from + "|" + to;

    /// <summary>Independent calculation: the session is walked turn by turn and the pairs are counted with simple sets.</summary>
    private static Dictionary<string, int> Oracle(IReadOnlyList<AgentEvent> events, FilePath? root)
    {
        Dictionary<string, int> weights = [];
        List<FilePath> reads = [];
        List<FilePath> changed = [];
        bool open = false;
        foreach (AgentEvent e in events)
        {
            bool main = e.Header.SubagentId is null;
            if ((e is UserMessage && main) || (e is TurnEnded && main))
            {
                reads.Clear();
                changed.Clear();
                open = e is UserMessage;
                continue;
            }
            FilePath? path = e is FileRead fr ? fr.Path : e is FileChanged fc ? fc.Path : null;
            if (path is not { } p || (root is { } rt && !p.IsUnder(rt))) continue;
            if (!open)
            {
                reads.Clear();
                changed.Clear();
                open = true;
            }
            if (e is FileRead)
            {
                if (!reads.Contains(p)) reads.Add(p);
            }
            else if (((FileChanged)e).CallId is not null && !changed.Contains(p))
            {
                changed.Add(p);
                foreach (FilePath rd in reads)
                {
                    if (rd != p) weights[Key("R", rd, p)] = weights.GetValueOrDefault(Key("R", rd, p)) + 1;
                }
                foreach (FilePath other in changed)
                {
                    if (other == p) continue;
                    // The order of the pairs is that of the path string (ordinal, like Java's toString().compareTo)
                    FilePath lo = string.CompareOrdinal(other.Value, p.Value) < 0 ? other : p;
                    FilePath hi = lo == other ? p : other;
                    weights[Key("C", lo, hi)] = weights.GetValueOrDefault(Key("C", lo, hi)) + 1;
                }
            }
        }
        return weights;
    }

    private static Dictionary<string, int> Actual(KnowledgeGraph g) =>
        g.Edges.ToDictionary(e => Key(e.Relation == KnowledgeGraph.Relation.ReadBeforeEdit ? "R" : "C", e.From, e.To), e => e.Weight);

    private static void AssertCoherent(KnowledgeGraph g, IReadOnlyList<AgentEvent> events, FilePath? root)
    {
        HashSet<FilePath> touched = [.. events
            .Select(e => e is FileRead r ? r.Path : e is FileChanged c ? c.Path : (FilePath?)null)
            .OfType<FilePath>()
            .Where(p => root is not { } rt || p.IsUnder(rt))];
        // A file on which only one call failed is also a node (plan 018), but without relations
        HashSet<string> failedCalls = [.. events.OfType<ToolResult>().Where(r => r.Error).Select(r => r.CallId)];
        HashSet<FilePath> failedOnly = [.. events.OfType<ToolCall>()
            .Where(c => failedCalls.Contains(c.CallId) && c.Target is { } t && (root is not { } rt || t.IsUnder(rt)))
            .Select(c => c.Target!.Value)
            .Where(p => !touched.Contains(p))];
        Assert.True(touched.Concat(failedOnly).ToHashSet().SetEquals(g.Nodes.Select(n => n.Path)), "one node per touched file (or one with an error)");
        foreach (KnowledgeGraph.Edge e in g.Edges)
        {
            Assert.True(touched.Contains(e.From) && touched.Contains(e.To), "edge endpoints");
            Assert.NotEqual(e.From, e.To); // no self-edges
            Assert.True(e.Weight >= 1 && e.Weight <= g.Turns, "weight " + e.Weight + " of " + g.Turns + " turns");
        }
        Assert.Equal(Oracle(events, root).OrderBy(x => x.Key), Actual(g).OrderBy(x => x.Key));
        var view = KnowledgeView.Of(g, false, KnowledgeView.MaxNodes);
        Dictionary<FilePath, ForceLayout.Point> pos = ForceLayout.Run(view, new Dictionary<FilePath, ForceLayout.Point>(), 150);
        Assert.Equal(view.Nodes.Count, pos.Count);
        foreach (ForceLayout.Point p in pos.Values) Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y));
    }

    [Fact]
    public void TheSyntheticSessionBecomesAGraphOfItsFiles()
    {
        FilePath root = Tmp.Combine("projects");
        FilePath project = Tmp.Combine("project");
        Directory.CreateDirectory(project.Value);
        FilePath folder = root.Combine(ClaudeProjectsLocator.Encode(project.Value));
        Directory.CreateDirectory(folder.Value);
        FilePath session = folder.Combine(Session + ".jsonl");
        File.Copy(TestSupport.Resource("claude/projects/D--fake-proj/" + Session + ".jsonl").Value, session.Value);
        IReadOnlyList<AgentEvent> events = SessionReader.ReadSession(new ClaudeCodeAdapter(root, false), session);

        KnowledgeGraph g = Build(events, null);
        Assert.NotEmpty(g.Nodes); // the synthetic trace touches files
        Assert.True(g.Nodes.Any(n => n.Reads > 0) && g.Nodes.Any(n => n.AgentChanges > 0));
        Assert.Contains(g.Nodes, n => n.External > 0); // there is an external change
        AssertCoherent(g, events, null);
    }

    [Fact]
    public void ARealSessionBecomesACoherentGraphOfTheProject()
    {
        RealTrace.Found real = RealTrace.LocateOrSkip();
        FilePath root = real.ProjectRoot;
        IReadOnlyList<AgentEvent> events = SessionReader.ReadSession(real.Adapter, real.Session);

        KnowledgeGraph g = Build(events, root);
        Assert.SkipWhen(g.Nodes.Count == 0, "the session does not touch files of this project");
        Assert.All(g.Nodes, n => Assert.True(n.Path.IsUnder(root)));
        long start = Stopwatch.GetTimestamp();
        AssertCoherent(g, events, root);
        long millis = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Console.WriteLine("KnowledgeGraphIT: " + g.Nodes.Count + " files, " + g.Edges.Count + " relations, " + g.Turns
            + " turns; cross-check and layout in " + millis + " ms");
        Assert.True(millis < 15_000, "took " + millis + " ms");
    }
}
