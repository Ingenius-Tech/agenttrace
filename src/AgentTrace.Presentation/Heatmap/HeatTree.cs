using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Presentation.Tree;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Heatmap;

/// <summary>
/// A file or a folder of the map. Folders sum what they contain.
/// <c>Size</c>: bytes on disk. <c>AreaBytes</c>: its weight in the "size" area (each file, at least 1 KiB).
/// <c>Kind</c>: last change (according to git or the agent), files only. <c>LastTurn</c>: last turn in which it was touched (−1 if none).
/// <c>Files</c>/<c>TouchedFiles</c>: files it contains (or 1) and how many have activity.
/// </summary>
public sealed class HeatNode
{
    private readonly List<HeatNode> _children = [];

    internal HeatNode(FilePath path, string name, bool directory)
    {
        Path = path;
        Name = name;
        Directory = directory;
    }

    public FilePath Path { get; }

    public string Name { get; }

    public bool Directory { get; }

    public bool Ghost { get; internal set; }

    public long Size { get; internal set; }

    public long AreaBytes { get; internal set; }

    public FileActivity Activity { get; internal set; }

    public ChangeKind? Kind { get; internal set; }

    /// <summary>The changes were made only by someone other than the agent.</summary>
    public bool OnlyExternal { get; internal set; }

    public int TurnsTouched { get; internal set; }

    public int LastTurn { get; internal set; } = -1;

    public int Files { get; internal set; }

    public int TouchedFiles { get; internal set; }

    public IReadOnlyList<HeatNode> Children => _children;

    internal void Add(HeatNode child) => _children.Add(child);
}

/// <summary>
/// The project as a tree of <see cref="HeatNode"/>. <c>LastTurn</c>: index of the last turn of the session (−1 if none).
/// <c>UnplacedErrors</c> and <c>TargetsKnown</c> come from the graph; <c>Truncated</c>, from the listing.
/// </summary>
public sealed class HeatTree
{
    public const long MinAreaBytes = 1024;

    private readonly Dictionary<FilePath, HeatNode> _nodes;

    private HeatTree(HeatNode root, Dictionary<FilePath, HeatNode> nodes, int lastTurn, int unplacedErrors, bool targetsKnown, bool truncated)
    {
        Root = root;
        _nodes = nodes;
        LastTurn = lastTurn;
        UnplacedErrors = unplacedErrors;
        TargetsKnown = targetsKnown;
        Truncated = truncated;
    }

    public HeatNode Root { get; }

    public int LastTurn { get; }

    public int UnplacedErrors { get; }

    public bool TargetsKnown { get; }

    public bool Truncated { get; }

    public HeatNode? Find(FilePath path) => _nodes.GetValueOrDefault(path);

    /// <summary>
    /// Joins the disk listing with what the agent has touched: a touched file that is not in the listing (ignored, deleted)
    /// gets in anyway, with size 0; what is outside the project does not. The state comes from git (<paramref name="index"/>) and, otherwise, from the graph.
    /// </summary>
    public static HeatTree Build(FilePath root, ProjectListing listing, KnowledgeGraph graph, FileStatusIndex index)
    {
        var nodes = new Dictionary<FilePath, HeatNode>();
        var rootNode = new HeatNode(root, root.FileName.Length == 0 ? root.Value : root.FileName, true);
        nodes[root] = rootNode;

        foreach (ListedFile file in listing.Files)
        {
            if (!file.Path.IsUnder(root) || file.Path == root) continue;
            HeatNode node = FileNode(file.Path, nodes);
            node.Size = file.Size;
            node.Ghost = file.Ghost;
            node.Kind = index.File(file.Path)?.Kind;
        }
        foreach (KnowledgeGraph.Node touched in graph.Nodes)
        {
            if (!touched.Path.IsUnder(root) || touched.Path == root) continue;
            HeatNode node = FileNode(touched.Path, nodes);
            node.Activity = new FileActivity(touched.Reads, touched.Created, touched.Modified, touched.Deleted, touched.External, touched.Errors);
            node.OnlyExternal = touched.OnlyExternal;
            node.TurnsTouched = touched.TurnsTouched;
            node.LastTurn = touched.LastTurn;
            node.Kind = index.File(touched.Path)?.Kind ?? touched.LastChange;
            node.Ghost |= node.Kind == ChangeKind.Deleted;
        }
        Total(rootNode);
        return new HeatTree(rootNode, nodes, graph.Turns - 1, graph.UnplacedErrors, graph.TargetsKnown, listing.Truncated);
    }

    /// <summary>The node of the file, creating (if missing) the intermediate folders up to the root.</summary>
    private static HeatNode FileNode(FilePath path, Dictionary<FilePath, HeatNode> nodes)
    {
        if (nodes.TryGetValue(path, out HeatNode? existing)) return existing;
        var node = new HeatNode(path, path.FileName, false);
        nodes[path] = node;
        FolderNode(path.Parent!.Value, nodes).Add(node);
        return node;
    }

    private static HeatNode FolderNode(FilePath path, Dictionary<FilePath, HeatNode> nodes)
    {
        if (nodes.TryGetValue(path, out HeatNode? existing)) return existing;
        var node = new HeatNode(path, path.FileName, true);
        nodes[path] = node;
        FolderNode(path.Parent!.Value, nodes).Add(node);
        return node;
    }

    private static void Total(HeatNode node)
    {
        if (!node.Directory)
        {
            node.AreaBytes = Math.Max(node.Size, MinAreaBytes);
            node.Files = 1;
            node.TouchedFiles = node.Activity.Total > 0 ? 1 : 0;
            return;
        }
        foreach (HeatNode child in node.Children)
        {
            Total(child);
            node.Size += child.Size;
            node.AreaBytes += child.AreaBytes;
            node.Activity = node.Activity.Plus(child.Activity);
            node.Files += child.Files;
            node.TouchedFiles += child.TouchedFiles;
            node.LastTurn = Math.Max(node.LastTurn, child.LastTurn);
        }
    }
}
