namespace OpenCrosshair;

/// <summary>The app icon (app.ico, made by tools/make-icon.ps1), built into the program.</summary>
internal static class AppIcon
{
    private static Stream Open() => typeof(AppIcon).Assembly.GetManifestResourceStream("OpenCrosshair.app.ico")!;

    /// <summary>All sizes; Windows picks the right one for title bars and Alt+Tab.</summary>
    public static Icon Large
    {
        get
        {
            using var stream = Open();
            return new Icon(stream);
        }
    }

    /// <summary>The small size, for the tray.</summary>
    public static Icon Small
    {
        get
        {
            using var stream = Open();
            return new Icon(stream, SystemInformation.SmallIconSize);
        }
    }
}
