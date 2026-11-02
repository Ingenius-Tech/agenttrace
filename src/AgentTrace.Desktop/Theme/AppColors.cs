using Avalonia.Media;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Desktop.Theme;

/// <summary>
/// The application palette (dark theme; contrast >= 5.5:1 on #1C1C1E and #2C2C2E). It is the only source of these values:
/// the Java version repeated them in <c>agenttrace.css</c> and in <c>GraphColors</c> and a test checked that they matched.
/// </summary>
public static class AppColors
{
    // File status colors
    public static readonly Color Created = Color.Parse("#56D364");
    public static readonly Color Modified = Color.Parse("#E2C08D");
    public static readonly Color Deleted = Color.Parse("#FF7B72");
    public static readonly Color Read = Color.Parse("#79C0FF");
    public static readonly Color Error = Color.Parse("#F85149");
    public static readonly Color Ignored = Color.Parse("#8B949E");
    public static readonly Color BadgeText = Color.Parse("#1C1C1E");

    // Surfaces and text
    public static readonly Color Background = Color.Parse("#1C1C1E");
    public static readonly Color Panel = Color.Parse("#232325");
    public static readonly Color Card = Color.Parse("#2C2C2E");
    public static readonly Color Border = Color.Parse("#636366");
    public static readonly Color BorderSoft = Color.Parse("#48484A");
    public static readonly Color Text = Color.Parse("#F2F2F7");
    public static readonly Color Muted = Color.Parse("#A1A1A6");
    public static readonly Color Select = Color.Parse("#FFFFFF");

    // Syntax highlighting (contrast >= 6:1 on #1C1C1E)
    public static readonly Color SyntaxKeyword = Color.Parse("#FF7B72");
    public static readonly Color SyntaxString = Color.Parse("#A5D6FF");
    public static readonly Color SyntaxComment = Color.Parse("#8B949E");
    public static readonly Color SyntaxNumber = Color.Parse("#79C0FF");

    public static IBrush Brush(Color color) => new SolidColorBrush(color);

    /// <summary>Color of a file's status: created, modified, deleted; with no change (null), the "read" one.</summary>
    public static Color Status(ChangeKind? kind) => kind switch
    {
        null => Read,
        ChangeKind.Created => Created,
        ChangeKind.Modified => Modified,
        ChangeKind.Deleted => Deleted,
        _ => throw new System.Diagnostics.UnreachableException(),
    };

    /// <summary>Linear interpolation between two colors (<paramref name="t"/> between 0 and 1).</summary>
    public static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));
}
