namespace AgentTrace.Desktop;

/// <summary>The Panorama counts as "in view" for the panes it shows: their refresh rules treat it like their own tab being selected.</summary>
public sealed partial class MainWindow
{
    /// <summary>The Knowledge pane is in view: its own tab, or the Panorama with its toggle on.</summary>
    private bool KnowledgeShown => _tabs.SelectedIndex == KnowledgeTab || (_tabs.SelectedIndex == PanoramaTab && _panorama.KnowledgeVisible);

    /// <summary>The Areas pane is in view: its own tab, or the Panorama with its toggle on.</summary>
    private bool AreasShown => _tabs.SelectedIndex == AreasTab || (_tabs.SelectedIndex == PanoramaTab && _panorama.AreasVisible);
}
