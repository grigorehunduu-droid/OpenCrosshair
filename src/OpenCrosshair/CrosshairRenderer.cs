using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenCrosshair;

/// <summary>
/// All bitmaps here are square and even-sized; the crosshair center is the pixel corner at (Width / 2, Height / 2).
/// Opacity isn't baked in: it's applied when the bitmap is displayed.
/// </summary>
internal static class CrosshairRenderer
{
    /// <summary>The style's custom image if it has one that loads, otherwise the drawn crosshair.</summary>
    public static Bitmap Render(CrosshairStyle s) => LoadImage(s) ?? Draw(s);

    /// <summary>First frame of <see cref="CrosshairStyle.ImagePath"/>, scaled; null if unset, missing or unreadable.</summary>
    public static Bitmap? LoadImage(CrosshairStyle s)
    {
        var frames = LoadImageFrames(s)?.Frames;
        if (frames is null) return null;
        foreach (var extra in frames.Skip(1)) extra.Dispose();
        return frames[0];
    }

    /// <summary>
    /// All frames of the style's image (one for PNG, several for an animated GIF) with each frame's delay in ms;
    /// null if unset, missing or unreadable.
    /// </summary>
    public static (List<Bitmap> Frames, List<int> DelaysMs)? LoadImageFrames(CrosshairStyle s)
    {
        const long maxFileBytes = 25 * 1024 * 1024;
        const int maxFrames = 300;
        const long maxPixels = 32L * 1024 * 1024;   // all frames together stay under ~128 MB of memory
        if (s.ImagePath is null) return null;
        try
        {
            var file = new FileInfo(s.ImagePath);
            if (!file.Exists || file.Length > maxFileBytes) return null;
            // Read into memory so the file isn't locked while the app runs.
            using var image = Image.FromStream(new MemoryStream(File.ReadAllBytes(file.FullName)));
            bool animated = image.FrameDimensionsList.Contains(FrameDimension.Time.Guid);
            int count = animated ? Math.Min(image.GetFrameCount(FrameDimension.Time), maxFrames) : 1;
            byte[]? delays = animated ? image.PropertyItems.FirstOrDefault(p => p.Id == 0x5100)?.Value : null;   // GIF frame delays, 1/100 s each
            var frames = new List<Bitmap>(count);
            var delaysMs = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0 && (long)(i + 1) * frames[0].Width * frames[0].Height > maxPixels) break;   // very long or large GIFs are cut short
                if (animated) image.SelectActiveFrame(FrameDimension.Time, i);
                frames.Add(Finish(Fit(image, s.ImageScalePercent / 100.0), s, image: true));
                int d = delays is { } v && v.Length >= 4 * (i + 1) ? BitConverter.ToInt32(v, 4 * i) * 10 : 100;
                delaysMs.Add(d < 20 ? 100 : d);   // like browsers: near-zero delays mean "default speed"
            }
            return (frames, delaysMs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return null;   // GDI+ reports unreadable images as ArgumentException / OutOfMemoryException
        }
    }

    private static Bitmap Fit(Image image, double scale)
    {
        const int maxSide = 1024;   // bigger images are shrunk to fit; a crosshair never needs more
        scale = Math.Min(scale, (double)maxSide / Math.Max(image.Width, image.Height));
        int w = Math.Max(1, (int)Math.Round(image.Width * scale)), h = Math.Max(1, (int)Math.Round(image.Height * scale));
        int side = Math.Max(w, h) + 1 & ~1;   // round up to even so the center lands on a pixel corner
        var canvas = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(image, (side - w) / 2, (side - h) / 2, w, h);
        return canvas;
    }

    /// <summary>Draws the crosshair, fully opaque, on a transparent canvas.</summary>
    public static Bitmap Draw(CrosshairStyle s)
    {
        int o = s.Outline ? s.OutlineThickness : 0;
        // Everything except the label fits in this radius at any rotation.
        int reach = new[]
        {
            s.Length > 0 ? s.Gap + s.Length : 0,
            s.CircleRadius > 0 ? s.CircleRadius + s.CircleThickness : 0,
            s.Shape != CrosshairShape.None ? s.Gap + s.ShapeSize + s.Thickness : 0,
            s.DotSize, s.Thickness,
        }.Max() + o;
        using var label = s.LabelText is null ? null : LabelPath(s.LabelText, s.LabelSize);
        var lb = label?.GetBounds() ?? RectangleF.Empty;
        int labelTop = reach + 4;
        int c = reach + 2;
        if (label is not null)
            c = Math.Max(c, (int)Math.Ceiling(Math.Max(labelTop + lb.Height, Math.Max(-lb.Left, lb.Right))) + o + 2);

        var bmp = new Bitmap(2 * c, 2 * c, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TranslateTransform(c, c);
            // Outlines first, under everything, so an outline never covers a neighbouring shape.
            if (o > 0) DrawShapes(g, s, s.OutlineColor, o);
            DrawShapes(g, s, s.Color, 0);
            if (label is not null)
            {
                g.ResetTransform();
                g.TranslateTransform(c, c + labelTop - lb.Top);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (o > 0)
                {
                    using var pen = new Pen(s.OutlineColor, 2 * o) { LineJoin = LineJoin.Round };
                    g.DrawPath(pen, label);
                }
                using var brush = new SolidBrush(s.Color);
                g.FillPath(brush, label);
            }
        }
        return Finish(bmp, s, image: false);
    }

    /// <summary>Draws every shape around the origin. grow &gt; 0 is the outline pass: shapes grow by that many px per side.</summary>
    private static void DrawShapes(Graphics g, CrosshairStyle s, Color color, int grow)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color) { LineJoin = LineJoin.Miter, StartCap = LineCap.Square, EndCap = LineCap.Square };
        int t = s.Thickness, h = t / 2;
        // Arms along the axes are drawn as whole pixels (sharp); any other angle is anti-aliased.
        bool axisAligned = s.Arms is 2 or 4 && s.Rotation % 90 == 0;

        if (s.Length > 0)
        {
            for (int i = 0; i < s.Arms; i++)
            {
                if (i == 0 && s.TStyle) continue;
                int angle = -90 + s.Rotation + i * 360 / s.Arms;   // first arm points up
                if (axisAligned)
                {
                    g.SmoothingMode = SmoothingMode.None;
                    var arm = ((angle % 360 + 360) % 360) switch
                    {
                        0 => new Rectangle(s.Gap, -h, s.Length, t),
                        90 => new Rectangle(-h, s.Gap, t, s.Length),
                        180 => new Rectangle(-s.Gap - s.Length, -h, s.Length, t),
                        _ => new Rectangle(-h, -s.Gap - s.Length, t, s.Length),
                    };
                    g.FillRectangle(brush, Rectangle.Inflate(arm, grow, grow));
                }
                else
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var state = g.Save();
                    g.RotateTransform(angle);
                    g.FillRectangle(brush, s.Gap - grow, -t / 2f - grow, s.Length + 2 * grow, t + 2 * grow);
                    g.Restore(state);
                }
            }
        }

        var rotated = g.Save();
        g.RotateTransform(s.Rotation);
        if (s.DotSize > 0)
        {
            var dot = Rectangle.Inflate(new Rectangle(-s.DotSize / 2, -s.DotSize / 2, s.DotSize, s.DotSize), grow, grow);
            g.SmoothingMode = s.RoundDot || s.Rotation % 90 != 0 ? SmoothingMode.AntiAlias : SmoothingMode.None;
            if (s.RoundDot) g.FillEllipse(brush, dot);
            else g.FillRectangle(brush, dot);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (s.CircleRadius > 0)
        {
            pen.Width = s.CircleThickness + 2 * grow;
            float r = s.CircleRadius;
            if (s.CircleArc >= 360) g.DrawEllipse(pen, -r, -r, 2 * r, 2 * r);
            else g.DrawArc(pen, -r, -r, 2 * r, 2 * r, 90 - s.CircleArc / 2f, s.CircleArc);
        }
        if (s.Shape != CrosshairShape.None)
        {
            pen.Width = t + 2 * grow;
            float k = s.ShapeSize / 2f;
            switch (s.Shape)
            {
                case CrosshairShape.Chevron:
                    g.DrawLines(pen, [new PointF(-k, s.Gap + k), new PointF(0, s.Gap), new PointF(k, s.Gap + k)]);
                    break;
                case CrosshairShape.Triangle:
                    g.DrawPolygon(pen, [Polar(k, -90), Polar(k, 30), Polar(k, 150)]);
                    break;
                case CrosshairShape.Square:
                    g.DrawRectangle(pen, -k, -k, 2 * k, 2 * k);
                    break;
            }
        }
        g.Restore(rotated);
    }

    private static PointF Polar(float radius, double degrees) =>
        new((float)(radius * Math.Cos(degrees * Math.PI / 180)), (float)(radius * Math.Sin(degrees * Math.PI / 180)));

    private static GraphicsPath LabelPath(string text, int size)
    {
        var path = new GraphicsPath();
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        var family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        path.AddString(text, family, (int)FontStyle.Bold, size, new PointF(0, 0), format);
        return path;
    }

    /// <summary>Applies the effects that work on finished pixels: rotation (images only; drawn shapes rotate themselves) and blur.</summary>
    private static Bitmap Finish(Bitmap bmp, CrosshairStyle s, bool image)
    {
        if (image && s.Rotation != 0) bmp = Replace(bmp, CrosshairAnimator.Rotate(bmp, s.Rotation));
        if (s.Blur > 0) bmp = Replace(bmp, Blur(bmp, s.Blur));
        return bmp;
    }

    private static Bitmap Replace(Bitmap old, Bitmap next)
    {
        old.Dispose();
        return next;
    }

    /// <summary>Two box-blur passes (close to a Gaussian) on a canvas padded so the glow isn't cut off.</summary>
    internal static Bitmap Blur(Bitmap src, int radius)
    {
        int pad = 2 * radius, w = src.Width + 2 * pad, h = src.Height + 2 * pad;
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result)) g.DrawImageUnscaled(src, pad, pad);
        // Premultiplied pixels, so transparent areas don't bleed black into the edges.
        var data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var px = new int[w * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            for (int pass = 0; pass < 2; pass++)
            {
                BoxBlur(px, w, h, radius, horizontal: true);
                BoxBlur(px, w, h, radius, horizontal: false);
            }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        }
        finally { result.UnlockBits(data); }
        return result;
    }

    private static void BoxBlur(int[] px, int w, int h, int r, bool horizontal)
    {
        var src = (int[])px.Clone();
        int n = 2 * r + 1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int a = 0, red = 0, green = 0, blue = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sx = horizontal ? x + k : x, sy = horizontal ? y : y + k;
                    if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;   // outside the canvas counts as transparent
                    int p = src[sy * w + sx];
                    a += (p >> 24) & 255; red += (p >> 16) & 255; green += (p >> 8) & 255; blue += p & 255;
                }
                px[y * w + x] = (a / n) << 24 | (red / n) << 16 | (green / n) << 8 | blue / n;
            }
    }
}

/// <summary>Turns a still crosshair into animation frames. Phase runs 0 → 1 once per cycle.</summary>
internal static class CrosshairAnimator
{
    public static double Phase(CrosshairStyle s, double seconds) => seconds * s.AnimationSpeed / 4.0 % 1.0;

    /// <summary>Opacity multiplier (0..1) for this phase.</summary>
    public static double Opacity(CrosshairStyle s, double phase) => s.Animation switch
    {
        CrosshairAnimation.Breathe => 0.35 + 0.65 * (0.5 + 0.5 * Math.Cos(2 * Math.PI * phase)),
        CrosshairAnimation.Blink => phase < 0.5 ? 1 : 0,
        _ => 1,
    };

    /// <summary>A new frame for animations that change pixels; null when the still image can be reused.</summary>
    public static Bitmap? Frame(Bitmap still, CrosshairStyle s, double phase, bool drawn) => s.Animation switch
    {
        CrosshairAnimation.Rotate => Rotate(still, 360 * phase),
        // A drawn crosshair is redrawn in rainbow colors (works for white too); an image's hues are shifted instead.
        CrosshairAnimation.Rainbow when drawn => RedrawInColor(s, Hue(360 * phase)),
        CrosshairAnimation.Rainbow => HueRotate(still, 360 * phase),
        _ => null,
    };

    /// <summary>Index of the GIF frame showing at <paramref name="ms"/>, looping forever.</summary>
    public static int FrameIndex(IReadOnlyList<int> delaysMs, double ms)
    {
        if (delaysMs.Count <= 1) return 0;
        double t = ms % delaysMs.Sum();
        for (int i = 0; i < delaysMs.Count; i++)
            if ((t -= delaysMs[i]) < 0) return i;
        return delaysMs.Count - 1;
    }

    /// <summary>Fully saturated color at <paramref name="degrees"/> on the color wheel (0 red, 120 green, 240 blue).</summary>
    public static Color Hue(double degrees)
    {
        double h = (degrees % 360 + 360) % 360 / 60;
        double x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch
        {
            0 => (1d, x, 0d), 1 => (x, 1d, 0d), 2 => (0d, 1d, x), 3 => (0d, x, 1d), 4 => (x, 0d, 1d), _ => (1d, 0d, x),
        };
        return Color.FromArgb(255, (int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    private static Bitmap RedrawInColor(CrosshairStyle s, Color color)
    {
        var copy = s.Clone();
        copy.Color = color;
        return CrosshairRenderer.Draw(copy);
    }

    /// <summary>Shoot animation: 1 right after a left click, easing back to 0 (speed 1 = 1.2 s, 10 = 0.12 s).</summary>
    public static double Spread(CrosshairStyle s, double secondsSinceShot)
    {
        double x = secondsSinceShot * s.AnimationSpeed / 1.2;
        return x is >= 0 and < 1 ? Math.Pow(1 - x, 3) : 0;
    }

    /// <summary>Shoot frame: a drawn crosshair opens its gap; an image grows up to 30 %. Null when at rest.</summary>
    public static Bitmap? Shoot(Bitmap still, CrosshairStyle s, double spread, bool drawn)
    {
        if (spread <= 0) return null;
        if (drawn)
        {
            var open = s.Clone();
            open.Gap += (int)Math.Round(spread * Math.Max(4, s.Length));
            return CrosshairRenderer.Draw(open);
        }
        float size = still.Width * (float)(1 + 0.3 * spread);
        int side = (int)Math.Ceiling(size) + 1 & ~1;
        var frame = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(frame);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(still, (side - size) / 2, (side - size) / 2, size, size);
        return frame;
    }

    internal static Bitmap Rotate(Bitmap still, double degrees)
    {
        // Big enough for the diagonal, so every angle fits and the frame size never changes.
        int side = (int)Math.Ceiling(still.Width * Math.Sqrt(2)) + 1 & ~1;
        var frame = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(frame);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.TranslateTransform(side / 2f, side / 2f);
        g.RotateTransform((float)degrees);
        g.DrawImage(still, -still.Width / 2f, -still.Height / 2f, still.Width, still.Height);
        return frame;
    }

    private static Bitmap HueRotate(Bitmap still, double degrees)
    {
        // Luminance-preserving hue rotation (same matrix as SVG feColorMatrix hueRotate), transposed for GDI+.
        double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        float F(double v) => (float)v;
        var m = new ColorMatrix(
        [
            [F(0.213 + cos * 0.787 - sin * 0.213), F(0.213 - cos * 0.213 + sin * 0.143), F(0.213 - cos * 0.213 - sin * 0.787), 0, 0],
            [F(0.715 - cos * 0.715 - sin * 0.715), F(0.715 + cos * 0.285 + sin * 0.140), F(0.715 - cos * 0.715 + sin * 0.715), 0, 0],
            [F(0.072 - cos * 0.072 + sin * 0.928), F(0.072 - cos * 0.072 - sin * 0.283), F(0.072 + cos * 0.928 + sin * 0.072), 0, 0],
            [0, 0, 0, 1, 0],
            [0, 0, 0, 0, 1],
        ]);
        var frame = new Bitmap(still.Width, still.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(frame);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(m);
        g.DrawImage(still, new Rectangle(0, 0, still.Width, still.Height), 0, 0, still.Width, still.Height, GraphicsUnit.Pixel, attributes);
        return frame;
    }
}
