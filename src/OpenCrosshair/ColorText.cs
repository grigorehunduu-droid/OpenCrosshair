using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using Color = System.Drawing.Color;

namespace OpenCrosshair;

/// <summary>
/// Draws label text with Windows' DirectWrite, which (unlike GDI+) can draw color emoji like 🔥 or 🎯.
/// </summary>
internal static class ColorText
{
    // One-entry cache: Shoot redraws the crosshair every frame, but the label rarely changes.
    private static (string Key, Bitmap? Bitmap)? _last;
    private static readonly object Gate = new();

    /// <summary>
    /// The text on a transparent bitmap cut to its visible pixels: emoji in full color, other text in
    /// <paramref name="color"/>, with an <paramref name="outline"/> px border in <paramref name="outlineColor"/>.
    /// Null if there's nothing to see or DirectWrite isn't available. The caller disposes the bitmap.
    /// </summary>
    public static Bitmap? Render(string text, float size, Color color, int outline, Color outlineColor)
    {
        var key = $"{text}\n{size}\n{color.ToArgb()}\n{outline}\n{outlineColor.ToArgb()}";
        lock (Gate)
        {
            if (_last?.Key != key)
            {
                _last?.Bitmap?.Dispose();
                _last = (key, Crop(Uncached(text, size, color, outline, outlineColor)));
            }
            return _last.Value.Bitmap?.Clone() as Bitmap;
        }
    }

    private static Bitmap? Uncached(string text, float size, Color color, int outline, Color outlineColor)
    {
        Bitmap? result;
        try
        {
            using var fill = Draw(text, size, color, emoji: true);
            if (outline == 0) result = new Bitmap(fill);
            else
            {
                // The outline: the same text as a solid shape in the outline color, stamped around the real text.
                using var shape = Draw(text, size, outlineColor, emoji: false);
                result = new Bitmap(fill.Width, fill.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(result);
                for (int dx = -outline; dx <= outline; dx++)
                    for (int dy = -outline; dy <= outline; dy++)
                        if (dx * dx + dy * dy <= outline * outline + outline) g.DrawImageUnscaled(shape, dx, dy);
                g.DrawImageUnscaled(fill, 0, 0);
            }
        }
        catch (Exception e) when (e is SharpGen.Runtime.SharpGenException or COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            result = null;   // no label rather than no crosshair
        }
        return result;
    }

    /// <summary>Cuts the bitmap down to its non-transparent pixels (disposing the original); null if it's empty.</summary>
    private static Bitmap? Crop(Bitmap? bmp)
    {
        if (bmp is null) return null;
        int w = bmp.Width, h = bmp.Height, left = w, top = h, right = -1, bottom = -1;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new int[w * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);   // 32bpp rows have no padding
        bmp.UnlockBits(data);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if ((px[y * w + x] >>> 24) != 0)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        using (bmp)
            return right < 0 ? null : bmp.Clone(Rectangle.FromLTRB(left, top, right + 1, bottom + 1), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private static Bitmap Draw(string text, float size, Color color, bool emoji)
    {
        int pad = (int)Math.Ceiling(size / 4) + 6;   // room for the outline and for glyphs that reach outside their box
        using var dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
        using var format = dwrite.CreateTextFormat("Segoe UI", null, FontWeight.Bold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size, "en-us");
        using var layout = dwrite.CreateTextLayout(text, format, 4096, 4096);
        var metrics = layout.Metrics;
        int width = (int)Math.Ceiling(metrics.WidthIncludingTrailingWhitespace) + 2 * pad;
        int height = (int)Math.Ceiling(metrics.Height) + 2 * pad;

        using var wic = new IWICImagingFactory();
        using var target = wic.CreateBitmap((uint)width, (uint)height, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        using var d2d = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        var properties = new RenderTargetProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));
        using (var rt = d2d.CreateWicBitmapRenderTarget(target, properties))
        {
            rt.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
            using var brush = rt.CreateSolidColorBrush(new Color4(color.R / 255f, color.G / 255f, color.B / 255f, 1f));
            rt.BeginDraw();
            rt.Clear(new Color4(0, 0, 0, 0));
            rt.DrawTextLayout(new Vector2(pad, pad), layout, brush, emoji ? DrawTextOptions.EnableColorFont : DrawTextOptions.None);
            rt.EndDraw();
        }

        // Copy the premultiplied pixels into a GDI+ bitmap of the same layout.
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var source = target.Lock(BitmapLockFlags.Read);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(source.Data.DataPointer + y * (int)source.Stride, row, 0, row.Length);
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
}
