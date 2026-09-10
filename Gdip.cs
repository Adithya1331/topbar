using System.Runtime.InteropServices;

namespace TopBar;

/// <summary>
/// Minimal GDI+ flat-API wrapper for anti-aliased circles and pills. GDI has no anti-aliasing,
/// so its ellipses look jagged; GDI+ is loaded lazily the first time the calendar opens.
/// Only shapes go through here; text is still drawn with GDI on the same DC afterwards.
/// </summary>
internal static class Gdip
{
    private const int SmoothingModeAntiAlias = 4;
    private const int PixelOffsetModeHalf = 4;
    private const int UnitPixel = 2;

    private static nint s_token;

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out nint token, ref StartupInput input, nint output);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateFromHDC(nint hdc, out nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll")] private static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll")] private static extern int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipDeletePen(nint pen);
    [DllImport("gdiplus.dll")] private static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipFillRectangle(nint graphics, nint brush, float x, float y, float w, float h);
    [DllImport("gdiplus.dll")] private static extern int GdipFillPolygonI(nint graphics, nint brush, Native.POINT[] points, int count, int fillMode);

    private static void EnsureStarted()
    {
        if (s_token != default) return;
        var input = new StartupInput { GdiplusVersion = 1 };
        _ = GdiplusStartup(out s_token, ref input, default);
    }

    /// <summary>COLORREF (0x00BBGGRR) to GDI+ ARGB (0xAARRGGBB), fully opaque.</summary>
    private static uint Argb(uint colorref)
        => 0xFF000000u | ((colorref & 0xFF) << 16) | (colorref & 0xFF00) | ((colorref >> 16) & 0xFF);

    /// <summary>Anti-aliased drawing surface over a GDI DC. Dispose before drawing GDI text on the same DC.</summary>
    internal sealed class Surface : IDisposable
    {
        private readonly nint _g;

        public Surface(nint hdc)
        {
            EnsureStarted();
            _ = GdipCreateFromHDC(hdc, out _g);
            _ = GdipSetSmoothingMode(_g, SmoothingModeAntiAlias);
            _ = GdipSetPixelOffsetMode(_g, PixelOffsetModeHalf);
        }

        public void FillCircle(uint colorref, float cx, float cy, float radius)
        {
            _ = GdipCreateSolidFill(Argb(colorref), out nint b);
            _ = GdipFillEllipse(_g, b, cx - radius, cy - radius, radius * 2, radius * 2);
            _ = GdipDeleteBrush(b);
        }

        public void DrawRing(uint colorref, float cx, float cy, float radius, float thickness)
        {
            _ = GdipCreatePen1(Argb(colorref), thickness, UnitPixel, out nint p);
            float r = radius - thickness / 2f;
            _ = GdipDrawEllipse(_g, p, cx - r, cy - r, r * 2, r * 2);
            _ = GdipDeletePen(p);
        }

        /// <summary>Horizontal pill from (x1,cy) to (x2,cy) with round caps of the given radius.</summary>
        public void FillPill(uint colorref, float x1, float x2, float cy, float radius)
        {
            _ = GdipCreateSolidFill(Argb(colorref), out nint b);
            _ = GdipFillEllipse(_g, b, x1 - radius, cy - radius, radius * 2, radius * 2);
            _ = GdipFillEllipse(_g, b, x2 - radius, cy - radius, radius * 2, radius * 2);
            if (x2 > x1) _ = GdipFillRectangle(_g, b, x1, cy - radius, x2 - x1, radius * 2);
            _ = GdipDeleteBrush(b);
        }

        public void FillRoundRect(uint colorref, float x, float y, float w, float h, float radius)
        {
            _ = GdipCreateSolidFill(Argb(colorref), out nint b);
            float d = radius * 2;
            _ = GdipFillEllipse(_g, b, x, y, d, d);
            _ = GdipFillEllipse(_g, b, x + w - d, y, d, d);
            _ = GdipFillEllipse(_g, b, x, y + h - d, d, d);
            _ = GdipFillEllipse(_g, b, x + w - d, y + h - d, d, d);
            _ = GdipFillRectangle(_g, b, x + radius, y, w - d, h);
            _ = GdipFillRectangle(_g, b, x, y + radius, w, h - d);
            _ = GdipDeleteBrush(b);
        }

        public void FillPolygon(uint colorref, Native.POINT[] points)
        {
            _ = GdipCreateSolidFill(Argb(colorref), out nint b);
            _ = GdipFillPolygonI(_g, b, points, points.Length, 0);
            _ = GdipDeleteBrush(b);
        }

        public void Dispose()
        {
            if (_g != default) _ = GdipDeleteGraphics(_g);
        }
    }
}
