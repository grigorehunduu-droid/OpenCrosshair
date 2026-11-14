namespace OpenCrosshair;

/// <summary>
/// A crosshair ready to show: its still image (several for a GIF) plus what's needed to animate it.
/// Used by both the on-screen overlay and the designer preview, so they always look the same.
/// </summary>
internal sealed class CrosshairSprite : IDisposable
{
    private readonly CrosshairStyle _style;   // a copy, so edits made later don't change a sprite that's on screen
    private readonly List<Bitmap> _stills;
    private readonly List<int> _delaysMs;
    private readonly bool _drawn;

    public CrosshairSprite(CrosshairStyle style)
    {
        _style = style.Clone();
        var image = CrosshairRenderer.LoadImageFrames(_style);
        ImageMissing = _style.ImagePath is not null && image is null;
        _drawn = image is null;
        (_stills, _delaysMs) = image ?? ([CrosshairRenderer.Draw(_style)], [0]);
    }

    /// <summary>An image was chosen but couldn't be loaded, so the drawn crosshair is shown instead.</summary>
    public bool ImageMissing { get; }

    public bool IsAnimated => _style.Animation != CrosshairAnimation.None || _stills.Count > 1;

    public Size StillSize => _stills[0].Size;

    /// <summary>
    /// The bitmap to show at <paramref name="seconds"/>, and an opacity multiplier (0..1).
    /// When <paramref name="owned"/> isn't null it's a new bitmap the caller must dispose; otherwise the
    /// returned bitmap belongs to this sprite.
    /// </summary>
    public Bitmap Frame(double seconds, double secondsSinceShot, out double opacity, out Bitmap? owned)
    {
        double phase = CrosshairAnimator.Phase(_style, seconds);
        var still = _stills[CrosshairAnimator.FrameIndex(_delaysMs, seconds * 1000)];
        owned = _style.Animation == CrosshairAnimation.Shoot
            ? CrosshairAnimator.Shoot(still, _style, CrosshairAnimator.Spread(_style, secondsSinceShot), _drawn)
            : CrosshairAnimator.Frame(still, _style, phase, _drawn);
        opacity = CrosshairAnimator.Opacity(_style, phase);
        return owned ?? still;
    }

    public void Dispose()
    {
        foreach (var bitmap in _stills) bitmap.Dispose();
    }
}
