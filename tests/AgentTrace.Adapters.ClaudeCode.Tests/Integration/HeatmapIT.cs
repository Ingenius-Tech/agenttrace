using System.Text.Json;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Tree;

namespace AgentTrace.Adapters.ClaudeCode.Tests.Integration;

/// <summary>
/// e2e of the heatmap without UI: Claude Code trace (with two failed <c>Edit</c>/<c>Write</c> and one failed <c>Bash</c>) →
/// adapter → <c>KnowledgeBuilder</c> → <c>HeatTree</c>. The counters are checked against an independent calculation that reads the JSON
/// of the trace directly, without going through the adapter or the graph builder.
/// </summary>
[Trait("Category", "Integration")]
public sealed class HeatmapIT : TempDirTest
{
    private const string Session = "bbbbbbbb-0000-0000-0000-000000000002";

    private static string Line(object o) => JsonSerializer.Serialize(o);

    private static string User(string text) =>
        Line(new { type = "user", sessionId = Session, message = new { role = "user", content = text } });

    private static string Call(string id, string tool, object input) =>
        Line(new { type = "assistant", sessionId = Session, message = new { role = "assistant", content = new object[] { new { type = "tool_use", id, name = tool, input } } } });

    private static string Result(string id, bool error, object? toolUseResult = null) =>
        Line(new { type = "user", sessionId = Session, message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = id, content = error ? "failed" : "ok", is_error = error } } }, toolUseResult });

    private static string TurnEnd() => Line(new { type = "system", subtype = "turn_duration", sessionId = Session, durationMs = 1000 });

    private static List<string> Trace(string a, string b, string c, string d) =>
    [
        User("first turn"),
        Call("t1", "Read", new { file_path = a }), Result("t1", false),
        Call("t2", "Edit", new { file_path = a, old_string = "x", new_string = "y" }), Result("t2", false, new { filePath = a, originalFile = "x" }),
        Call("t3", "Edit", new { file_path = b, old_string = "x", new_string = "y" }), Result("t3", true),
        Call("t4", "Read", new { file_path = c }), Result("t4", false),
        Call("t5", "Bash", new { command = "false" }), Result("t5", true),
        TurnEnd(),
        User("second turn"),
        Call("t6", "Edit", new { file_path = b, old_string = "x", new_string = "y" }), Result("t6", true),
        Call("t7", "Edit", new { file_path = b, old_string = "x", new_string = "y" }), Result("t7", false, new { filePath = b, originalFile = "x" }),
        Call("t8", "Write", new { file_path = d, content = "z" }), Result("t8", true),
        Call("t9", "Read", new { file_path = a }), Result("t9", false),
        TurnEnd(),
    ];

    /// <summary>Independent calculation: reads the JSON of the trace and counts by hand per file (without the adapter). Per file: reads, writes, errors.</summary>
    private static (Dictionary<string, int[]> PerFile, int Unplaced) Oracle(IEnumerable<string> lines)
    {
        Dictionary<string, int[]> perFile = [];
        Dictionary<string, (string Tool, string? Path)> calls = [];
        int unplaced = 0;
        foreach (string line in lines)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("message", out JsonElement message) || message.GetProperty("content").ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement block in message.GetProperty("content").EnumerateArray())
            {
                string type = block.GetProperty("type").GetString()!;
                if (type == "tool_use")
                {
                    string? path = block.GetProperty("input").TryGetProperty("file_path", out JsonElement p) ? p.GetString() : null;
                    calls[block.GetProperty("id").GetString()!] = (block.GetProperty("name").GetString()!, path);
                }
                else if (type == "tool_result")
                {
                    (string tool, string? path) = calls[block.GetProperty("tool_use_id").GetString()!];
                    bool error = block.GetProperty("is_error").GetBoolean();
                    if (error && path is null)
                    {
                        unplaced++;
                        continue;
                    }
                    if (path is null) continue;
                    if (!perFile.TryGetValue(path, out int[]? counts)) perFile[path] = counts = new int[3];
                    if (error) counts[2]++;
                    else if (tool == "Read") counts[0]++;
                    else counts[1]++;
                }
            }
        }
        return (perFile, unplaced);
    }

    [Fact]
    public void ASessionWithFailedEditsBecomesAHeatTreeThatMatchesAnIndependentCount()
    {
        FilePath project = Tmp.Combine("project");
        FilePath src = project.Combine("src");
        Directory.CreateDirectory(src.Value);
        FilePath a = src.Combine("a.cs"), b = src.Combine("b.cs"), c = src.Combine("c.cs"), d = project.Combine("d.cs"), e = project.Combine("e.md");
        File.WriteAllText(a.Value, "aaaa");
        File.WriteAllText(c.Value, "cc");
        File.WriteAllText(e.Value, "e");
        List<string> lines = Trace(a.Value, b.Value, c.Value, d.Value);

        FilePath projects = Tmp.Combine("projects");
        FilePath folder = projects.Combine(ClaudeProjectsLocator.Encode(project.Value));
        Directory.CreateDirectory(folder.Value);
        FilePath session = folder.Combine(Session + ".jsonl");
        File.WriteAllLines(session.Value, lines);

        IReadOnlyList<AgentEvent> events = SessionReader.ReadSession(new ClaudeCodeAdapter(projects, false), session);
        var builder = new KnowledgeBuilder(project);
        foreach (AgentEvent ev in events) builder.Accept(ev);
        KnowledgeGraph graph = builder.Snapshot();
        FileStatusIndex index = FileStatusIndex.Empty(project);
        HeatTree tree = HeatTree.Build(project, ProjectScanner.Scan(project, IgnoreRules.WithoutGit(project), index, 1000), graph, index);

        (Dictionary<string, int[]> expected, int unplaced) = Oracle(lines);
        Assert.Equal(1, unplaced);
        Assert.Equal(unplaced, graph.UnplacedErrors);
        Assert.Equal(unplaced, tree.UnplacedErrors);
        Assert.True(tree.TargetsKnown);
        Assert.Equal(2, tree.LastTurn + 1);

        // Each touched file matches the independent calculation and the untouched ones stay cold
        foreach ((string path, int[] counts) in expected)
        {
            HeatNode node = tree.Find(new FilePath(path)) ?? throw new Xunit.Sdk.XunitException("falta " + path);
            Assert.Equal(counts[0], node.Activity.Reads);
            Assert.Equal(counts[1], node.Activity.Writes);
            Assert.Equal(counts[2], node.Activity.Errors);
        }
        Assert.Equal(0, tree.Find(e)!.Activity.Total);
        Assert.Equal(1, tree.Find(b)!.Activity.Writes); // the first Edit of b fails and does not count as a write; the second does
        Assert.Equal(2, tree.Find(b)!.Activity.Errors);
        Assert.Equal(1, tree.Find(d)!.Activity.Errors); // failed and not existing on disk: it enters as touched, with size 0
        Assert.Equal(0, tree.Find(d)!.Size);

        // Folders add up, and with only errors active just b and d have heat
        Assert.Equal(expected.Values.Sum(v => v[0]), tree.Root.Activity.Reads);
        Assert.Equal(expected.Values.Sum(v => v[1]), tree.Root.Activity.Writes);
        Assert.Equal(expected.Values.Sum(v => v[2]), tree.Root.Activity.Errors);
        var errorsOnly = new ActivityFilter(false, false, true);
        Assert.Equal(3, tree.Root.Activity.Value(errorsOnly));
        Assert.Equal(2, tree.Find(src)!.Activity.Value(errorsOnly));
        Assert.Equal(4, tree.Root.TouchedFiles);
        TreemapLayout view = TreemapLayout.Of(tree.Root, 800, 600, HeatArea.Activity, errorsOnly);
        HashSet<FilePath> shown = [.. view.Cells.Where(x => !x.Node.Directory).Select(x => x.Node.Path)];
        Assert.True(shown.SetEquals([b, d]), "with area = activity and errors only, b and d are shown");
    }
}
