using AgentTrace.Presentation.Observation;
using AgentTrace.Presentation.Panorama;

namespace AgentTrace.Desktop;

/// <summary>
/// The Panorama counts as "in view" for the panes it shows: their refresh rules treat it like their own tab being selected. While it is
/// shown, selecting something in one pane highlights what is related in the other two (<see cref="FocusHub"/>); in the separate tabs nothing links.
/// </summary>
public sealed partial class MainWindow
{
    private readonly FocusHub _focusHub = new();

    /// <summary>The Knowledge pane is in view: its own tab, or the Panorama with its toggle on.</summary>
    private bool KnowledgeShown => _tabs.SelectedIndex == KnowledgeTab || (_tabs.SelectedIndex == PanoramaTab && _panorama.KnowledgeVisible);

    /// <summary>The Areas pane is in view: its own tab, or the Panorama with its toggle on.</summary>
    private bool AreasShown => _tabs.SelectedIndex == AreasTab || (_tabs.SelectedIndex == PanoramaTab && _panorama.AreasVisible);

    /// <summary>The linked focus of the Panorama (tests read what is shown; the window is the only publisher).</summary>
    public FocusHub LinkedFocus => _focusHub;

    /// <summary>Connects what each pane publishes (the user's selections) to the hub, once, when the window is built.</summary>
    private void WirePanorama()
    {
        _graph.Canvas.Selected += selection => Publish(selection is null ? null : selection.IsStep ? TraceFocus.ForStep(selection.CallId!) : TraceFocus.ForGraphTurn(selection.TurnIndex));
        _areas.Canvas.CellSelectionChanged += () => Publish(AreasCellFocus() ?? AreasTaskFocus());
        _areas.Canvas.TaskSelectionChanged += () => Publish(AreasTaskFocus() ?? AreasCellFocus());
        _areas.Canvas.TurnHeaderClicked += turn => Publish(TraceFocus.ForAreasTurn(turn));
        _knowledge.Canvas.Selected += file => Publish(file is { } path ? TraceFocus.ForFile(path) : null);
        _panorama.ClearFocusRequested += ClearLinkedFocus;
    }

    private TraceFocus? AreasCellFocus() =>
        _areas.Canvas.SelectedCell is { } cell
            ? TraceFocus.ForCell(_areas.Canvas.Matrix.Rows[cell.Row].Key, _areas.Canvas.Matrix.Columns[cell.Column].Turn)
            : null;

    private TraceFocus? AreasTaskFocus() => _areas.Canvas.SelectedTask is { } task ? TraceFocus.ForTask(task) : null;

    /// <summary>
    /// <c>--panorama</c> shows only the named panes for this run (the saved layout is not touched until the user changes it) and
    /// <c>--panorama-focus</c> selects what the matching pane would select, so the panes link when the Panorama is shown.
    /// </summary>
    public void ApplyPanoramaOptions(StartupOptions options)
    {
        if (options.Panorama is { } panes)
        {
            _panorama.ApplyLayout(_panorama.Layout.WithPanes(panes));
            if (KnowledgeShown) RefreshKnowledge();
            if (AreasShown) RefreshAreas();
        }
        if (options.PanoramaFocus is not { } spec) return;
        if (spec.StartsWith("task:", StringComparison.OrdinalIgnoreCase)) _areas.SetPending(spec, options.AreasExpand);
        else if (spec.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) || spec is "last" or "last-error") SelectInGraph(spec);
        else
        {
            // a call id or a file: whichever exists in the data is selected
            SelectInGraph(spec);
            SelectInKnowledge(spec);
        }
    }

    /// <summary>The Panorama was shown or left: only while shown do selections link; the highlights go away when it is left.</summary>
    private void SetPanoramaShown(bool shown)
    {
        _focusHub.Active = shown;
        if (!shown)
        {
            ClearLinkedFocus();
            return;
        }
        // What is already selected links too (the selection may predate the Panorama: a startup option, or made in the pane's own tab)
        Publish(_graph.Canvas.Selection is { } selection
            ? selection.IsStep ? TraceFocus.ForStep(selection.CallId!) : TraceFocus.ForGraphTurn(selection.TurnIndex)
            : AreasCellFocus() ?? AreasTaskFocus() ?? (_knowledge.Canvas.SelectedPath is { } file ? TraceFocus.ForFile(file) : null));
    }

    private void Publish(TraceFocus? focus)
    {
        if (_focusHub.Publish(focus, f => ShowLinkedFocus(f, reveal: true))) _panorama.FocusActive = _focusHub.Current is not null;
    }

    /// <summary>Removes every highlight and forgets the focus (the native selections of the panes stay).</summary>
    private void ClearLinkedFocus()
    {
        _focusHub.Clear();
        _panorama.FocusActive = false;
        _graph.Canvas.ShowFocus(null);
        _areas.Canvas.ShowFocus(null);
        _knowledge.Canvas.ShowFocus(null);
    }

    /// <summary>
    /// The new data of a pane arrived: the current focus is resolved again against it (once per refresh, only if there is one) without
    /// scrolling anything, so a live session keeps its highlights.
    /// </summary>
    private void ReapplyLinkedFocus()
    {
        if (!_focusHub.Active || _focusHub.Current is not { } focus) return;
        ShowLinkedFocus(focus, reveal: false);
    }

    /// <summary>The pane the focus comes from shows nothing of it; the other two highlight what it links to. None of these calls raises a selection.</summary>
    private void ShowLinkedFocus(TraceFocus? focus, bool reveal)
    {
        if (focus is null)
        {
            ClearLinkedFocus();
            return;
        }
        FocusTargets targets = FocusLinks.Of(focus, _graph.Canvas.Graph, _lastChanges, _areas.Canvas.Matrix);
        ShowIn(focus.Origin, null, reveal);
        foreach (FocusOrigin pane in FocusHub.ConsumersOf(focus.Origin)) ShowIn(pane, targets, reveal);
    }

    private void ShowIn(FocusOrigin pane, FocusTargets? targets, bool reveal)
    {
        switch (pane)
        {
            case FocusOrigin.Graph:
                _graph.Canvas.ShowFocus(targets, reveal);
                break;
            case FocusOrigin.Areas:
                _areas.Canvas.ShowFocus(targets, reveal);
                break;
            default:
                _knowledge.Canvas.ShowFocus(targets?.Files, reveal);
                break;
        }
    }
}
