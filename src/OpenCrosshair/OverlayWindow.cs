namespace OpenCrosshair;

/// <summary>
/// Borderless, click-through, never-focused, always-on-top window that shows one per-pixel-alpha bitmap
/// through UpdateLayeredWindow. Nothing is painted the normal WinForms way.
/// </summary>
internal sealed class OverlayWindow : Form
{
    private Bitmap? _image;
    private byte _alpha = 255;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "OpenCrosshair overlay";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>
    /// Shows <paramref name="image"/> centered on <paramref name="screen"/> plus an offset.
    /// The caller owns the bitmap and must keep it alive until the next SetImage.
    /// </summary>
    public void SetImage(Bitmap image, byte alpha, Rectangle screen, Point offset)
    {
        _image = image;
        _alpha = alpha;
        int half = image.Width / 2;
        Bounds = new Rectangle(screen.X + screen.Width / 2 + offset.X - half,
            screen.Y + screen.Height / 2 + offset.Y - half, image.Width, image.Height);
        Push();
    }

    private void Push()
    {
        if (_image is null || !IsHandleCreated) return;
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr bitmap = _image.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(memDc, bitmap);
        try
        {
            var dst = new Native.POINT { X = Left, Y = Top };
            var size = new Native.SIZE { Cx = _image.Width, Cy = _image.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = _alpha, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) Push();
    }

    /// <summary>Other topmost windows (and some games) can push us down; called on a timer to come back up.</summary>
    public void KeepOnTop()
    {
        if (IsHandleCreated && Visible)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
