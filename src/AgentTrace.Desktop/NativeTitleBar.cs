using System.Runtime.InteropServices;

namespace AgentTrace.Desktop;

/// <summary>
/// Sets the native title bar to the same color as the application (DWM, Windows only). Port of the Java version:
/// immersive dark mode and, on Windows 11, caption, border and text colors. On other systems it does nothing and if something
/// fails the bar stays as it was.
/// </summary>
public static partial class NativeTitleBar
{
    private const int DwmwaUseImmersiveDarkModeOld = 19; // Windows 10 up to 1903
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>Windows COLORREF: <c>0x00BBGGRR</c>.</summary>
    public static int ColorRef(int rgb)
    {
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        return (b << 16) | (g << 8) | r;
    }

    /// <returns>whether Windows accepted dark mode (false on other systems or if something fails).</returns>
    public static bool ApplyDark(nint hwnd, int captionRgb, int textRgb)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return false;
        try
        {
            var dark = Set(hwnd, DwmwaUseImmersiveDarkMode, 1) == 0
                || Set(hwnd, DwmwaUseImmersiveDarkModeOld, 1) == 0;
            // Only Windows 11 understands these three; on Windows 10 they return an error that is ignored
            Set(hwnd, DwmwaCaptionColor, ColorRef(captionRgb));
            Set(hwnd, DwmwaBorderColor, ColorRef(captionRgb));
            Set(hwnd, DwmwaTextColor, ColorRef(textRgb));
            return dark;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static int Set(nint hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
