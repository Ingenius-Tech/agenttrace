using static AgentTrace.Presentation.Graph.GraphGeometry;

namespace AgentTrace.Presentation.Graph;

/// <summary>
/// Which part of the graph is visible: scroll and zoom, and "follow the end". No Avalonia. The scroll is in world
/// coordinates, so it does not change on zoom. Following the end is active while the view is at the end of the graph
/// and is turned off when scrolling back or selecting something earlier.
/// </summary>
public sealed class GraphViewport
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 2.0;

    /// <summary>Below this zoom the cards are drawn with only a shape and an icon.</summary>
    public const double DetailZoom = 0.6;

    private double _viewW = 800;
    private double _viewH = 600;
    private double _contentW;
    private double _contentH;

    /// <summary>The long axis of the graph: X when horizontal (default) or Y when vertical. It is the axis that follows the end.</summary>
    private bool _vertical;

    public double Zoom { get; private set; } = 1.0;

    public double ScrollX { get; private set; }

    public double ScrollY { get; private set; }

    public bool Following { get; private set; } = true;

    public bool Detailed => Zoom >= DetailZoom;

    /// <summary>Changes the axis that follows the end; following is reactivated and the view goes to the end of the new axis.</summary>
    public void SetVertical(bool vertical)
    {
        if (_vertical == vertical) return;
        _vertical = vertical;
        GoToEnd();
    }

    /// <summary>Window size in screen pixels.</summary>
    public void SetViewSize(double w, double h)
    {
        _viewW = Math.Max(w, 1);
        _viewH = Math.Max(h, 1);
        Clamp();
    }

    /// <summary>Graph size in world coordinates; if following the end, the view tracks it.</summary>
    public void SetContentSize(double w, double h)
    {
        _contentW = w;
        _contentH = h;
        if (Following) FollowEnd();
        Clamp();
    }

    /// <summary>The part of the world visible now.</summary>
    public Rect VisibleRect() => new(ScrollX, ScrollY, _viewW / Zoom, _viewH / Zoom);

    public double ToWorldX(double screenX) => ScrollX + screenX / Zoom;

    public double ToWorldY(double screenY) => ScrollY + screenY / Zoom;

    public double ToScreenX(double worldX) => (worldX - ScrollX) * Zoom;

    public double ToScreenY(double worldY) => (worldY - ScrollY) * Zoom;

    /// <summary>User scroll, in world coordinates.</summary>
    public void ScrollBy(double dx, double dy)
    {
        ScrollX += dx;
        ScrollY += dy;
        Clamp();
        Following = AtEnd();
    }

    public void ScrollTo(double x, double y) => ScrollBy(x - ScrollX, y - ScrollY);

    /// <summary>Changes the zoom keeping the given screen point fixed.</summary>
    public void ZoomBy(double factor, double anchorScreenX, double anchorScreenY) => SetZoom(Zoom * factor, anchorScreenX, anchorScreenY);

    public void SetZoom(double value, double anchorScreenX, double anchorScreenY)
    {
        double worldX = ToWorldX(anchorScreenX);
        double worldY = ToWorldY(anchorScreenY);
        Zoom = Math.Max(MinZoom, Math.Min(MaxZoom, value));
        ScrollX = worldX - anchorScreenX / Zoom;
        ScrollY = worldY - anchorScreenY / Zoom;
        Clamp();
        if (Following) FollowEnd();
        else Following = AtEnd();
    }

    /// <summary>Goes back to the end and reactivates following.</summary>
    public void GoToEnd()
    {
        Following = true;
        FollowEnd();
        Clamp();
    }

    /// <summary>
    /// Makes sure the box is visible (centers it if outside). Selecting something that is not at the end turns off
    /// following; if it is the last one, it stays on.
    /// </summary>
    public void Reveal(Box box, double contentEnd)
    {
        Rect r = VisibleRect();
        if (box.X < r.X || box.Right > r.Right) ScrollX = box.X + box.W / 2.0 - r.W / 2;
        if (box.Y < r.Y || box.Bottom > r.Bottom) ScrollY = box.Y + box.H / 2.0 - r.H / 2;
        Clamp();
        Following = (_vertical ? box.Bottom : box.Right) >= contentEnd - 1 && AtEnd();
    }

    private bool AtEnd() => _vertical ? ScrollY >= MaxScrollY() - 1 : ScrollX >= MaxScrollX() - 1;

    private void FollowEnd()
    {
        if (_vertical) ScrollY = MaxScrollY();
        else ScrollX = MaxScrollX();
    }

    public double MaxScrollX() => Math.Max(0, _contentW - _viewW / Zoom);

    public double MaxScrollY() => Math.Max(0, _contentH - _viewH / Zoom);

    private void Clamp()
    {
        ScrollX = Math.Max(0, Math.Min(ScrollX, MaxScrollX()));
        ScrollY = Math.Max(0, Math.Min(ScrollY, MaxScrollY()));
    }
}
