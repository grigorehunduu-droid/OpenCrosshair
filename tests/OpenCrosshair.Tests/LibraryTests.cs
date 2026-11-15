using System.Text;
using System.Text.Json;

namespace OpenCrosshair.Tests;

/// <summary>One crosshair in library/crosshairs.json.</summary>
public sealed class LibraryEntry
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Code { get; set; } = "";
}

/// <summary>
/// The community library lives in this repository (library/crosshairs.json). These tests check every entry, and
/// can rebuild the gallery page: set OC_LIBRARY_WRITE=1 and run the tests to regenerate library/README.md and its images.
/// </summary>
public class LibraryTests
{
    private static string LibraryFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "library", "crosshairs.json"))) return Path.Combine(dir.FullName, "library");
        throw new DirectoryNotFoundException("library/crosshairs.json not found above " + AppContext.BaseDirectory);
    }

    private static List<LibraryEntry> Load() =>
        JsonSerializer.Deserialize<List<LibraryEntry>>(File.ReadAllText(Path.Combine(LibraryFolder(), "crosshairs.json")))!;

    [Fact]
    public void EveryLibraryCrosshairImports()
    {
        var entries = Load();
        Assert.NotEmpty(entries);
        foreach (var e in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name), "every crosshair needs a name");
            Assert.False(string.IsNullOrWhiteSpace(e.Author), $"{e.Name}: needs an author");
            Assert.True(CrosshairStyle.TryParseShareCode(e.Code, out _), $"{e.Name}: the share code doesn't import");
        }
        Assert.Equal(entries.Count, entries.Select(e => e.Name.ToLowerInvariant()).Distinct().Count());   // names are unique
    }

    [Fact]
    public void WriteLibraryPage()
    {
        if (Environment.GetEnvironmentVariable("OC_LIBRARY_WRITE") != "1") return;
        var folder = LibraryFolder();
        var images = Path.Combine(folder, "images");
        Directory.CreateDirectory(images);
        foreach (var old in Directory.GetFiles(images, "*.png")) File.Delete(old);

        var page = new StringBuilder();
        page.AppendLine("# Community crosshair library").AppendLine();
        page.AppendLine("Crosshairs shared by OpenCrosshair users. To use one: copy its share code, open **Customize**, paste it into");
        page.AppendLine("the **Share code** box and press **Import**.").AppendLine();
        page.AppendLine("**Share yours:** in the customize window, click **Share yours in the library…** (or");
        page.AppendLine("[open the form](../../../issues/new?template=crosshair.yml)). Accepted crosshairs are added to");
        page.AppendLine("[crosshairs.json](crosshairs.json) and appear here.").AppendLine();
        page.AppendLine("| | Crosshair | Share code |").AppendLine("|---|---|---|");
        foreach (var e in Load())
        {
            CrosshairStyle.TryParseShareCode(e.Code, out var style);
            var file = Slug(e.Name) + ".png";
            SavePreview(style, Path.Combine(images, file));
            var tags = string.IsNullOrWhiteSpace(e.Tags) ? "" : $"<br><sub>{Escape(e.Tags)}</sub>";
            page.AppendLine($"| <img src=\"images/{file}\" width=\"96\" alt=\"\"> | **{Escape(e.Name)}**<br><sub>by {Escape(e.Author)}</sub>{tags} | `{e.Code}` |");
        }
        File.WriteAllText(Path.Combine(folder, "README.md"), page.ToString());
    }

    private static string Slug(string name) =>
        string.Concat(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')).Trim('-');

    // Names and tags come from submissions: keep them from breaking the Markdown table or adding HTML.
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("|", "&#124;").Replace("\n", " ");

    private static void SavePreview(CrosshairStyle style, string path)
    {
        const int size = 128;
        using var sheet = new Bitmap(size, size);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(40, 44, 52));
        using var crosshair = CrosshairRenderer.Render(style);
        int zoom = Math.Max(1, Math.Min(3, (size - 8) / crosshair.Width));
        int side = crosshair.Width * zoom;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(crosshair, (size - side) / 2, (size - side) / 2, side, side);
        sheet.Save(path);
    }
}
