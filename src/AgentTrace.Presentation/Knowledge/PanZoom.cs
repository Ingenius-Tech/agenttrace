namespace AgentTrace.Presentation.Knowledge;

/// <summary>Free zoom and pan of the knowledge graph canvas: <c>screen = world * scale + translation</c>. No Avalonia.</summary>
public sealed class PanZoom
{
    public const double MinScale = 0.1;
    public const double MaxScale = 4.0;

    public double Scale { get; private set; } = 1.0;

    public double TranslateX { get; private set; }

    public double TranslateY { get; private set; }

    public double ToWorldX(double sx) => (sx - TranslateX) / Scale;

    public double ToWorldY(double sy) => (sy - TranslateY) / Scale;

    public double ToScreenX(double wx) => wx * Scale + TranslateX;

    public double ToScreenY(double wy) => wy * Scale + TranslateY;

    public void PanBy(double dxScreen, double dyScreen)
    {
        TranslateX += dxScreen;
        TranslateY += dyScreen;
    }

    /// <summary>Changes the scale keeping the given screen point fixed.</summary>
    public void ZoomAt(double factor, double sx, double sy)
    {
        double wx = ToWorldX(sx);
        double wy = ToWorldY(sy);
        Scale = Math.Max(MinScale, Math.Min(MaxScale, Scale * factor));
        TranslateX = sx - wx * Scale;
        TranslateY = sy - wy * Scale;
    }

    public void SetScale(double value, double sx, double sy) => ZoomAt(value / Scale, sx, sy);

    /// <summary>Fits a world rectangle in the window, with margin (without enlarging beyond 1.5×).</summary>
    public void Fit(double minX, double minY, double maxX, double maxY, double viewW, double viewH, double margin)
    {
        double w = Math.Max(maxX - minX, 1);
        double h = Math.Max(maxY - minY, 1);
        Scale = Math.Max(MinScale, Math.Min(1.5, Math.Min((viewW - 2 * margin) / w, (viewH - 2 * margin) / h)));
        TranslateX = viewW / 2 - (minX + maxX) / 2 * Scale;
        TranslateY = viewH / 2 - (minY + maxY) / 2 * Scale;
    }

    /// <summary>Centers a world point in the window without changing the scale.</summary>
    public void CenterOn(double wx, double wy, double viewW, double viewH)
    {
        TranslateX = viewW / 2 - wx * Scale;
        TranslateY = viewH / 2 - wy * Scale;
    }
}
