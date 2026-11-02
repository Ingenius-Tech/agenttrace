using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AgentTrace.Core;
using AgentTrace.Desktop.Theme;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using AgentTrace.Presentation.Tree;

namespace AgentTrace.Desktop.Controls;

/// <summary>File tree of the project, IDE style: lazy loading, icons, statuses and refresh without losing the selection.</summary>
public sealed class ProjectTreeView : UserControl
{
    private readonly TreeView _tree = new();
    private readonly TreeContext _context = new(IgnoreRules.WithoutGit(new FilePath(".")), FileStatusIndex.Empty(new FilePath(".")));
    private readonly Action<FilePath> _onOpen;
    private readonly Action<FilePath> _onShowChanges;
    private readonly ObservableCollection<FileTreeNode> _roots = [];
    private FileTreeNode? _root;
    private bool _autoExpanded;

    /// <param name="onOpen">Double click or Enter on a file.</param>
    /// <param name="onShowChanges">"View changes" of the context menu (only if the file has changes).</param>
    public ProjectTreeView(Action<FilePath> onOpen, Action<FilePath> onShowChanges)
    {
        _onOpen = onOpen;
        _onShowChanges = onShowChanges;
        _tree.ItemsSource = _roots;
        _tree.ItemTemplate = new FuncTreeDataTemplate<FileTreeNode>(
            (node, _) => new FileTreeRow(node, _context),
            node => node.Children);
        // The expansion of each row lives in the node: this way it is kept on refresh
        var expansion = new Style(x => x.OfType<TreeViewItem>());
        expansion.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(FileTreeNode.IsExpanded)) { Mode = BindingMode.TwoWay }));
        _tree.Styles.Add(expansion);
        _tree.DoubleTapped += (_, _) => OpenSelected();
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) OpenSelected();
        };
        _tree.ContextMenu = BuildContextMenu();

        var layout = new DockPanel { Background = AppColors.Brush(AppColors.Background) };
        Control toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(_tree);
        Content = layout;
    }

    public TreeContext Context => _context;

    public FileNode? SelectedNode => (_tree.SelectedItem as FileTreeNode)?.IsPlaceholder == false ? ((FileTreeNode)_tree.SelectedItem!).Node : null;

    /// <summary>Shows a new project, for now without knowing what git ignores or what has changed.</summary>
    public void SetProject(FilePath projectDir)
    {
        _autoExpanded = false;
        _context.Update(IgnoreRules.WithoutGit(projectDir), FileStatusIndex.Empty(projectDir));
        _root = new FileTreeNode(new FileNode(projectDir, true, false, false), _context, null);
        _root.IsExpanded = true;
        _roots.Clear();
        _roots.Add(_root);
    }

    /// <summary>Applies new rules and statuses, and lists the already loaded folders again. Must be called from the UI thread.</summary>
    public void Update(IgnoreRules rules, FileStatusIndex index)
    {
        if (_root is null) return;
        _context.Update(rules, index);
        _root.Reload();
        // The first time there are changes, the tree opens by itself down to them (if there are not too many)
        if (!_autoExpanded && TreeExpansion.ShouldAutoExpand(index.ChangedPaths().Count))
        {
            _autoExpanded = true;
            ExpandToChanges();
        }
        _root.Refresh();
    }

    /// <summary>Opens all the folders that lead to a changed file.</summary>
    public void ExpandToChanges()
    {
        if (_root is null) return;
        ExpandAncestors(_context.Index.ChangedPaths());
    }

    /// <summary>Closes all folders (except the root).</summary>
    public void CollapseAll()
    {
        if (_root is null) return;
        _root.CollapseLoaded();
        _root.IsExpanded = true;
    }

    /// <summary>Only the changed (or read) files and the folders that lead to them.</summary>
    public void SetChangedOnly(bool changedOnly)
    {
        _context.ChangedOnly = changedOnly;
        _root?.Reload();
        _root?.Refresh();
    }

    /// <summary>Opens the folders down to the file, selects it and makes it visible.</summary>
    public void Reveal(FilePath path)
    {
        ExpandAncestors([path]);
        if (Find(path) is not { } node) return;
        _tree.SelectedItem = node;
        Dispatcher.UIThread.Post(() => _tree.TreeContainerFromItem(node)?.BringIntoView(), DispatcherPriority.Background);
    }

    /// <summary>All the rows visible right now (for tests).</summary>
    public IReadOnlyList<FileTreeNode> VisibleNodes()
    {
        List<FileTreeNode> list = [];
        void Walk(FileTreeNode node)
        {
            list.Add(node);
            if (!node.IsExpanded) return;
            foreach (FileTreeNode child in node.Children.Where(c => !c.IsPlaceholder)) Walk(child);
        }
        if (_root is not null) Walk(_root);
        return list;
    }

    private void ExpandAncestors(IEnumerable<FilePath> targets)
    {
        if (_root is null) return;
        foreach (FilePath dir in TreeExpansion.DirectoriesToExpand(_root.Node.Path, targets))
        {
            if (Find(dir) is { } item) item.IsExpanded = true;
        }
    }

    /// <summary>Finds a node walking the path from the root (loads the folders it goes through).</summary>
    private FileTreeNode? Find(FilePath path)
    {
        if (_root is null) return null;
        FilePath baseDir = _root.Node.Path;
        if (!path.IsUnder(baseDir)) return null;
        FileTreeNode current = _root;
        if (path == baseDir) return current;
        foreach (string part in path.RelativeTo(baseDir).Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        {
            FileTreeNode? next = current.Child(part);
            if (next is null) return null;
            current = next;
        }
        return current;
    }

    private void OpenSelected()
    {
        if (SelectedNode is { Directory: false } node) _onOpen(node.Path);
    }

    private Control BuildToolbar()
    {
        Button expand = IconButton(Codicon.ExpandAll, TextStrings.TreeExpandToChanges);
        expand.Click += (_, _) => ExpandToChanges();
        Button collapse = IconButton(Codicon.CollapseAll, TextStrings.TreeCollapseAll);
        collapse.Click += (_, _) => CollapseAll();
        var onlyChanges = new ToggleButton { Content = Icon(Codicon.Filter) };
        ToolTip.SetTip(onlyChanges, TextStrings.TreeOnlyChangesTooltip);
        onlyChanges.Click += (_, _) => SetChangedOnly(onlyChanges.IsChecked == true);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { expand, collapse, onlyChanges },
        };
    }

    private ContextMenu BuildContextMenu()
    {
        var open = new MenuItem { Header = TextStrings.ActionOpen };
        open.Click += (_, _) => OpenSelected();
        var showChanges = new MenuItem { Header = TextStrings.ActionShowChanges };
        showChanges.Click += (_, _) =>
        {
            if (SelectedNode is { } node) _onShowChanges(node.Path);
        };
        var copyPath = new MenuItem { Header = TextStrings.ActionCopyPath };
        copyPath.Click += async (_, _) =>
        {
            if (SelectedNode is { } node && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(node.Path.Value);
            }
        };
        var menu = new ContextMenu { Items = { open, showChanges, copyPath } };
        menu.Opening += (_, _) =>
        {
            FileNode? node = SelectedNode;
            open.IsEnabled = node is { Directory: false };
            showChanges.IsEnabled = node is not null && NodeDecoration.Of(node, _context.Index).HasChange;
            copyPath.IsEnabled = node is not null;
        };
        return menu;
    }

    private static Button IconButton(Codicon icon, string tooltip)
    {
        var button = new Button { Content = Icon(icon) };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private static TextBlock Icon(Codicon icon) => new()
    {
        Text = Codicons.Glyph(icon),
        FontFamily = Codicons.Font,
        FontSize = 14,
    };
}
