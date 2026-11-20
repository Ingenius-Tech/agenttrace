using Avalonia.Controls;

namespace AgentTrace.Desktop.Controls;

/// <summary>
/// The permanent content of a tab that holds one pane. An Avalonia control has a single parent, so the Panorama takes the pane out of its slot
/// while it is shown and gives it back when another tab is selected; the tab itself never changes its content.
/// </summary>
public sealed class PaneSlot : Border
{
    public PaneSlot(Control pane)
    {
        Pane = pane;
        Child = pane;
    }

    public Control Pane { get; }

    public bool Holds => ReferenceEquals(Child, Pane);

    public void Take() => Child = null;

    public void Return() => Child = Pane;
}
