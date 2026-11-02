namespace AgentTrace.Presentation.Knowledge;

public sealed record Point3(double X, double Y, double Z);

/// <summary>A world point already projected: screen position, depth (greater = farther) and pixels per world unit at that distance.</summary>
public readonly record struct Projected(double X, double Y, double Depth, double Scale, bool Visible);

/// <summary>
/// Perspective orbital camera for the 3D knowledge graph. The world has time on Z (upwards); X and Y are those of the
/// 2D layout, with Y downwards in the "Top" view (same as 2D). Pure: no Avalonia.
/// </summary>
public sealed class Camera3D
{
    public const double MinDistance = 50;
    public const double MaxDistance = 20000;
    public const double MaxPitch = Math.PI / 2;
    private const double FieldOfView = Math.PI / 3;
    private const double Near = 1;

    public double TargetX { get; private set; }
    public double TargetY { get; private set; }
    public double TargetZ { get; private set; }

    /// <summary>Rotation around the Z axis, in radians.</summary>
    public double Yaw { get; private set; } = -Math.PI / 2;

    /// <summary>Elevation above the XY plane, in radians: 0 = from the side, π/2 = from above.</summary>
    public double Pitch { get; private set; } = Math.PI / 6;

    public double Distance { get; private set; } = 1000;

    public void SetView(double yaw, double pitch)
    {
        Yaw = yaw;
        Pitch = Math.Clamp(pitch, -MaxPitch, MaxPitch);
    }

    /// <summary>From above: same as 2D.</summary>
    public void TopView() => SetView(-Math.PI / 2, MaxPitch);

    /// <summary>From the side: time is vertical.</summary>
    public void SideView() => SetView(-Math.PI / 2, 0);

    public void FreeView() => SetView(-Math.PI / 3, Math.PI / 6);

    public void Orbit(double dyaw, double dpitch) => SetView(Yaw + dyaw, Pitch + dpitch);

    public void Zoom(double factor) => Distance = Math.Clamp(Distance / factor, MinDistance, MaxDistance);

    public void SetTarget(double x, double y, double z)
    {
        TargetX = x;
        TargetY = y;
        TargetZ = z;
    }

    /// <summary>Moves the target in the screen plane (<c>dxPixels</c> to the right, <c>dyPixels</c> downwards), dragging the content.</summary>
    public void Pan(double dxPixels, double dyPixels, double viewHeight)
    {
        double perPixel = Distance / Focal(viewHeight);
        (double rx, double ry, double rz) = Right();
        (double ux, double uy, double uz) = Up();
        // Dragging the content to the right moves the target to the left
        TargetX -= (rx * dxPixels - ux * dyPixels) * perPixel;
        TargetY += (ry * dxPixels - uy * dyPixels) * perPixel; // Y is inverted when projecting
        TargetZ -= (rz * dxPixels - uz * dyPixels) * perPixel;
    }

    /// <summary>Centers the camera on the box and backs off just enough for it to fit.</summary>
    public void Fit(Point3 min, Point3 max, double viewWidth, double viewHeight)
    {
        SetTarget((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        double radius = Math.Sqrt(Sq(max.X - min.X) + Sq(max.Y - min.Y) + Sq(max.Z - min.Z)) / 2;
        double half = Math.Min(viewWidth, viewHeight) / 2;
        double focal = Focal(viewHeight);
        Distance = Math.Clamp(Math.Max(radius, 1) * focal / Math.Max(half - 40, 40), MinDistance, MaxDistance);
    }

    public Projected Project(double x, double y, double z, double viewWidth, double viewHeight)
    {
        (double ex, double ey, double ez) = Eye();
        (double rx, double ry, double rz) = Right();
        (double ux, double uy, double uz) = Up();
        double dx = x - ex;
        double dy = -y - ey; // World Y points down: inverted so "Top" matches 2D
        double dz = z - ez;
        (double fx, double fy, double fz) = Forward();
        double depth = dx * fx + dy * fy + dz * fz;
        if (depth < Near) return new Projected(0, 0, depth, 0, false);
        double k = Focal(viewHeight) / depth;
        double sx = viewWidth / 2 + (dx * rx + dy * ry + dz * rz) * k;
        double sy = viewHeight / 2 - (dx * ux + dy * uy + dz * uz) * k;
        return new Projected(sx, sy, depth, k, true);
    }

    private double Focal(double viewHeight) => viewHeight / 2 / Math.Tan(FieldOfView / 2);

    // The target is also inverted in Y (same space as the projected point)
    private (double X, double Y, double Z) Eye()
    {
        double cp = Math.Cos(Pitch);
        return (TargetX + Distance * cp * Math.Cos(Yaw), -TargetY + Distance * cp * Math.Sin(Yaw), TargetZ + Distance * Math.Sin(Pitch));
    }

    private (double X, double Y, double Z) Forward()
    {
        double cp = Math.Cos(Pitch);
        return (-cp * Math.Cos(Yaw), -cp * Math.Sin(Yaw), -Math.Sin(Pitch));
    }

    private (double X, double Y, double Z) Right() => (-Math.Sin(Yaw), Math.Cos(Yaw), 0);

    private (double X, double Y, double Z) Up()
    {
        (double rx, double ry, double rz) = Right();
        (double fx, double fy, double fz) = Forward();
        return (ry * fz - rz * fy, rz * fx - rx * fz, rx * fy - ry * fx);
    }

    private static double Sq(double v) => v * v;
}
