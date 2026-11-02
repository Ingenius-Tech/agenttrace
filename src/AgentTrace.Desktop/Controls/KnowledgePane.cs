using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Desktop.Theme;
using AgentTrace.Presentation.Knowledge;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Controls;

/// <summary>The knowledge graph tab: a bar with filters and search, the canvas and the panel of the selected file.</summary>
public sealed class KnowledgePane : UserControl
{
    public const double DetailWidth = FileDetailPanel.PanelWidth;

    private readonly KnowledgeCanvasView _canvas = new();
    private readonly TextBlock _summary = new() { Text = MapStrings.NoFilesYet, Foreground = AppColors.Brush(AppColors.Muted), VerticalAlignment = VerticalAlignment.Center };
    private readonly FileDetailPanel _detail;
    private readonly StackPanel _views3D;
    private readonly ToggleButton _view3DToggle;
    private FilePath? _projectRoot;

    /// <param name="openFile">Opens a file in the viewer.</param>
    /// <param name="showDiff">Shows its change in the Inspector.</param>
    public KnowledgePane(Action<FilePath> openFile, Action<FilePath> showDiff)
    {
        _detail = new FileDetailPanel(openFile, showDiff, path => _canvas.Select(path), () => _canvas.Select(null));

        Button fit = Labelled(Codicon.ScreenFull, MapStrings.KnowledgeFit);
        ToolTip.SetTip(fit, MapStrings.KnowledgeFitTip);
        fit.Click += (_, _) => _canvas.Fit();
        var onlyChanged = new ToggleButton { Content = LabelledContent(Codicon.Diff, MapStrings.KnowledgeOnlyChanged) };
        ToolTip.SetTip(onlyChanged, MapStrings.KnowledgeOnlyChangedTip);
        onlyChanged.Click += (_, _) =>
        {
            _canvas.SetOnlyChanged(onlyChanged.IsChecked == true);
            RefreshSummary();
        };
        var repeated = new ToggleButton { Content = LabelledContent(Codicon.Link, MapStrings.KnowledgeRepeated) };
        ToolTip.SetTip(repeated, MapStrings.KnowledgeRepeatedTip);
        repeated.Click += (_, _) => _canvas.SetRepeatedOnly(repeated.IsChecked == true);
        var view3D = new ToggleButton { Content = LabelledContent(Codicon.Files, MapStrings.Knowledge3D) };
        ToolTip.SetTip(view3D, MapStrings.Knowledge3DTip);
        var top = Labelled(Codicon.ScreenFull, MapStrings.KnowledgeTop);
        var side = Labelled(Codicon.ScreenFull, MapStrings.KnowledgeSide);
        ToolTip.SetTip(top, MapStrings.KnowledgeTopTip);
        ToolTip.SetTip(side, MapStrings.KnowledgeSideTip);
        top.Click += (_, _) => _canvas.SetPreset("top");
        side.Click += (_, _) => _canvas.SetPreset("side");
        var trails = new ToggleButton { Content = LabelledContent(Codicon.Pulse, MapStrings.KnowledgeTrails), IsChecked = _canvas.ShowTrails };
        ToolTip.SetTip(trails, MapStrings.KnowledgeTrailsTip);
        trails.Click += (_, _) => _canvas.SetTrails(trails.IsChecked == true);
        _views3D = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false, Children = { top, side, trails } };
        view3D.Click += (_, _) => ShowView(view3D.IsChecked == true ? "3d" : "2d");
        _view3DToggle = view3D;
        var search = new TextBox { PlaceholderText = MapStrings.KnowledgeSearchPlaceholder, Width = 200 };
        search.TextChanged += (_, _) => _canvas.SetQuery(search.Text);

        var left = new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Children = { fit, onlyChanged, repeated, view3D, _views3D, search } };
        var bar = new DockPanel { Margin = new Thickness(8, 4) };
        DockPanel.SetDock(_summary, Dock.Right);
        bar.Children.Add(_summary);
        bar.Children.Add(left);

        var content = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        content.Children.Add(bar);
        DockPanel.SetDock(_detail, Dock.Right);
        content.Children.Add(_detail);
        content.Children.Add(_canvas);
        Content = content;

        _canvas.Selected += _ => RefreshDetail();
        _canvas.Activated += openFile;
    }

    public KnowledgeCanvasView Canvas => _canvas;

    public bool DetailVisible => _detail.HasDetail;

    public string Summary => _summary.Text ?? "";

    public IEnumerable<string> DetailTexts => _detail.Texts;

    /// <summary><c>2d</c>, <c>3d</c>, <c>top</c> or <c>side</c> (the last three, in 3D with that camera).</summary>
    public void ShowView(string name)
    {
        bool is3D = name != "2d";
        _canvas.SetMode3D(is3D);
        _view3DToggle.IsChecked = is3D;
        _views3D.IsVisible = is3D;
        if (is3D) _canvas.SetPreset(name == "3d" ? "free" : name);
    }

    public void SetProjectRoot(FilePath? root)
    {
        _projectRoot = root;
        _canvas.SetProjectRoot(root);
    }

    public void SetGraph(KnowledgeGraph graph)
    {
        _canvas.SetGraph(graph);
        RefreshSummary();
        RefreshDetail();
    }

    private void RefreshSummary()
    {
        KnowledgeGraph g = _canvas.Graph;
        int files = _canvas.DrawableNodes;
        _summary.Text = string.Format(CultureInfo.CurrentCulture, MapStrings.KnowledgeSummaryFormat,
            Counted(files, MapStrings.CountFiles_One, MapStrings.CountFiles_Other),
            Counted(g.Edges.Count, MapStrings.CountRelations_One, MapStrings.CountRelations_Other),
            Counted(g.Turns, MapStrings.CountTurns_One, MapStrings.CountTurns_Other));
    }

    private static string Counted(int n, string one, string other) => string.Format(CultureInfo.CurrentCulture, Plural.Pick(n, one, other), n);

    private void RefreshDetail() =>
        _detail.Update(_canvas.SelectedPath is { } selected ? NodeDetail.Of(_canvas.Graph, selected, _projectRoot) : null, _projectRoot);

    private static Control LabelledContent(Codicon icon, string text) => FileDetailPanel.LabelledContent(icon, text);

    private static Button Labelled(Codicon icon, string text) => FileDetailPanel.Labelled(icon, text);
}
