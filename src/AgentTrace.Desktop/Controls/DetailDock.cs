using Avalonia;
using Avalonia.Controls;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// Places a detail panel at the right of its pane when the pane is wide enough for both, and at the bottom (capped height, scrollable)
/// when it is not, so the panes keep working side by side in the Panorama and in small windows.
/// </summary>
public static class DetailDock
{
    public const double BottomMaxHeight = 260;

    /// <param name="dockChild">The direct child of the pane's <see cref="DockPanel"/> (receives the dock side).</param>
    /// <param name="panel">The bordered panel that is sized (the same control as <paramref name="dockChild"/> unless it is wrapped in a host).</param>
    /// <param name="paneWidth">The current width of the whole pane.</param>
    /// <param name="panelWidth">The width of the panel when docked at the right.</param>
    public static void Apply(Control dockChild, Border panel, double paneWidth, double panelWidth)
    {
        bool right = paneWidth >= 2 * panelWidth;
        DockPanel.SetDock(dockChild, right ? Dock.Right : Dock.Bottom);
        panel.Width = right ? panelWidth : double.NaN;
        panel.MaxHeight = right ? double.PositiveInfinity : BottomMaxHeight;
        panel.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 1, 0, 0);
    }
}
