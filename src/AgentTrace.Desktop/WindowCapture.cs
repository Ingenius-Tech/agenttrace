using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgentTrace.Desktop;

/// <summary>Window capture to PNG: <c>RenderTargetBitmap</c> (cross-platform) or <c>PrintWindow</c> (Windows only, includes the native bar).</summary>
public static partial class WindowCapture
{
    /// <summary>Draws the window's visual tree (without the native title bar).</summary>
    public static void SaveRenderTarget(Window window, string path)
    {
        var scale = window.RenderScaling;
        var size = new PixelSize(Math.Max(1, (int)(window.Bounds.Width * scale)), Math.Max(1, (int)(window.Bounds.Height * scale)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        Save(bitmap, path);
    }

    /// <summary>Captures the whole window (with the native bar) via GDI <c>PrintWindow</c>; Windows only.</summary>
    public static bool SavePrintWindow(Window window, string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect)) return false;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;

        var screenDc = GetDC(0);
        var memDc = CreateCompatibleDC(screenDc);
        var info = new BitmapInfo
        {
            Size = Marshal.SizeOf<BitmapInfo>(),
            Width = width,
            Height = -height, // top-down
            Planes = 1,
            BitCount = 32,
        };
        var dib = CreateDIBSection(screenDc, ref info, 0, out var bits, 0, 0);
        try
        {
            var old = SelectObject(memDc, dib);
            var ok = PrintWindow(hwnd, memDc, PwRenderFullContent);
            SelectObject(memDc, old);
            if (!ok) return false;
            using var bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, bits, new PixelSize(width, height), new Vector(96, 96), width * 4);
            Save(bitmap, path);
            return true;
        }
        finally
        {
            DeleteObject(dib);
            DeleteDC(memDc);
            ReleaseDC(0, screenDc);
        }
    }

    private static void Save(Bitmap bitmap, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    private const uint PwRenderFullContent = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);
}
