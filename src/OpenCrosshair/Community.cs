using System.Diagnostics;

namespace OpenCrosshair;

/// <summary>
/// The community library is a page in the GitHub repository. The app only opens it (and the share form) in your
/// browser; it never goes online itself.
/// </summary>
internal static class Community
{
    public const string Repository = "https://github.com/grigorehunduu-droid/OpenCrosshair";
    public const string LibraryUrl = Repository + "/blob/main/library/README.md";

    /// <summary>The "share a crosshair" form, filled in with this crosshair's name and share code (no picture, no games).</summary>
    public static string ShareUrl(CrosshairStyle style) =>
        Repository + "/issues/new?template=crosshair.yml"
        + "&title=" + Uri.EscapeDataString("Crosshair: " + style.Name)
        + "&name=" + Uri.EscapeDataString(style.Name)
        + "&code=" + Uri.EscapeDataString(style.ToShareCode());

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
