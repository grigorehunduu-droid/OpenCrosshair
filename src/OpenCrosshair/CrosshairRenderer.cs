using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenCrosshair;

internal static class CrosshairRenderer
{
    /// <summary>
    /// Draws the crosshair, fully opaque, on a transparent square canvas of even size.
    /// The crosshair center is the pixel corner at (Width / 2, Height / 2); opacity is applied when displayed.
    /// </summary>
    public static Bitmap Render(CrosshairStyle s)
    {
        int o = s.Outline ? s.OutlineThickness : 0;
        int arms = s.Length > 0 ? s.Gap + s.Length : 0;
        int circle = s.CircleRadius > 0 ? s.CircleRadius + s.CircleThickness : 0;
        int c = Math.Max(Math.Max(arms, circle), Math.Max(s.DotSize, s.Thickness)) + o + 2;

        var bmp = new Bitmap(2 * c, 2 * c, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);

        var rects = new List<Rectangle>();
        int t = s.Thickness, h = t / 2;
        if (s.Length > 0)
        {
            if (!s.TStyle) rects.Add(new Rectangle(c - h, c - s.Gap - s.Length, t, s.Length));
            rects.Add(new Rectangle(c - h, c + s.Gap, t, s.Length));
            rects.Add(new Rectangle(c - s.Gap - s.Length, c - h, s.Length, t));
            rects.Add(new Rectangle(c + s.Gap, c - h, s.Length, t));
        }
        if (s.DotSize > 0) rects.Add(new Rectangle(c - s.DotSize / 2, c - s.DotSize / 2, s.DotSize, s.DotSize));
        var ring = s.CircleRadius > 0
            ? new RectangleF(c - s.CircleRadius, c - s.CircleRadius, 2 * s.CircleRadius, 2 * s.CircleRadius)
            : RectangleF.Empty;

        // Outlines first, under everything, so an outline never covers a neighbouring shape.
        if (o > 0)
        {
            using var outline = new SolidBrush(s.OutlineColor);
            foreach (var r in rects) g.FillRectangle(outline, Rectangle.Inflate(r, o, o));
            if (!ring.IsEmpty) DrawRing(g, s.OutlineColor, s.CircleThickness + 2 * o, ring);
        }
        using var fill = new SolidBrush(s.Color);
        foreach (var r in rects) g.FillRectangle(fill, r);
        if (!ring.IsEmpty) DrawRing(g, s.Color, s.CircleThickness, ring);
        return bmp;
    }

    private static void DrawRing(Graphics g, Color color, float width, RectangleF bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, width);
        g.DrawEllipse(pen, bounds);
        g.SmoothingMode = SmoothingMode.None;
    }
}
