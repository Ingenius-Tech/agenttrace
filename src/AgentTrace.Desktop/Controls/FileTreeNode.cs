using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentTrace.Core;
using AgentTrace.Presentation.Tree;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Node of the file tree (Java's <c>FileTreeItem</c>). It lists its folder when first expanded and can be
/// refreshed without losing state: nodes that still exist are kept; only those that appear,
/// disappear or change are touched.
/// </summary>
public sealed class FileTreeNode : INotifyPropertyChanged
{
    private readonly TreeContext _context;
    private FileNode? _node;
    private bool _loaded;
    private bool _expanded;

    public FileTreeNode(FileNode node, TreeContext context, FileTreeNode? parent)
    {
        _node = node;
        _context = context;
        Parent = parent;
        // A folder that is not loaded carries a placeholder child: this way the control shows its expand arrow
        if (node.Expandable) Children.Add(new FileTreeNode(context));
    }

    /// <summary>The placeholder child of a folder not listed yet.</summary>
    private FileTreeNode(TreeContext context)
    {
        _context = context;
        IsPlaceholder = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Something that affects how the row is painted has changed (the node, its expansion or the context).</summary>
    public event Action? Changed;

    public FileTreeNode? Parent { get; }

    public bool IsPlaceholder { get; }

    public bool IsRoot => Parent is null && !IsPlaceholder;

    public FileNode Node => _node ?? throw new InvalidOperationException("The placeholder node has no file");

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value) return;
            _expanded = value;
            if (value) EnsureLoaded();
            Raise();
            Changed?.Invoke();
        }
    }

    /// <summary>Lists the folder the first time it is needed.</summary>
    public void EnsureLoaded()
    {
        if (_loaded || IsPlaceholder || !Node.Expandable) return;
        _loaded = true;
        SyncChildren(List([]));
    }

    /// <summary>Lists this folder again and, recursively, those that had already been loaded.</summary>
    public void Reload()
    {
        if (!_loaded) return;
        Dictionary<FilePath, FileTreeNode> existing = Children.Where(c => !c.IsPlaceholder).ToDictionary(c => c.Node.Path);
        List<FileTreeNode> next = List(existing);
        SyncChildren(next);
        foreach (FileTreeNode child in next) child.Reload();
        Changed?.Invoke();
    }

    /// <summary>Closes this folder and all those below it that had been loaded, without loading any new one.</summary>
    public void CollapseLoaded()
    {
        IsExpanded = false;
        if (!_loaded) return;
        foreach (FileTreeNode child in Children) child.CollapseLoaded();
    }

    /// <summary>The context (rules, statuses, filter) has changed: the rows are painted again.</summary>
    public void Refresh()
    {
        Changed?.Invoke();
        if (!_loaded) return;
        foreach (FileTreeNode child in Children) child.Refresh();
    }

    /// <summary>The child with that name, loading this folder if needed.</summary>
    public FileTreeNode? Child(string name)
    {
        EnsureLoaded();
        return Children.FirstOrDefault(c => !c.IsPlaceholder && c.Node.Name == name);
    }

    private List<FileTreeNode> List(Dictionary<FilePath, FileTreeNode> existing)
    {
        List<FileTreeNode> items = [];
        IReadOnlySet<string>? onChangedPaths = _context.ChangedOnly ? _context.Index.ChildNamesOnChangedPaths(Node.Path) : null;
        foreach (FileNode node in DirectoryLister.Children(Node.Path, _context.Rules, _context.Index))
        {
            if (onChangedPaths is not null && !onChangedPaths.Contains(node.Name)) continue;
            if (!existing.TryGetValue(node.Path, out FileTreeNode? item) || item.Node.Expandable != node.Expandable)
            {
                item = new FileTreeNode(node, _context, this);
            }
            else if (!item.Node.Equals(node))
            {
                item._node = node;
                item.Changed?.Invoke();
            }
            items.Add(item);
        }
        return items;
    }

    private void SyncChildren(List<FileTreeNode> next)
    {
        if (Children.Count == next.Count && Children.Zip(next).All(p => ReferenceEquals(p.First, p.Second))) return;
        Children.Clear();
        foreach (FileTreeNode child in next) Children.Add(child);
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
