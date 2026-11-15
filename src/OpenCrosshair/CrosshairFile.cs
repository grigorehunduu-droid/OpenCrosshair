using System.Security.Cryptography;
using System.Text.Json;

namespace OpenCrosshair;

/// <summary>
/// A crosshair in one shareable file (.ochair): every setting plus the custom picture, if it has one.
/// Share codes can't carry pictures; these files can.
/// </summary>
internal static class CrosshairFile
{
    public const string Extension = ".ochair";
    public const string Filter = "OpenCrosshair files (*.ochair)|*.ochair";
    private const long MaxFileBytes = 40 * 1024 * 1024;   // a 25 MB picture grows by a third as text

    private sealed class Contents
    {
        public int Version { get; set; } = 1;
        public CrosshairStyle? Style { get; set; }
        public string? ImageName { get; set; }
        public string? Image { get; set; }   // the picture file's bytes, base64
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(string path, CrosshairStyle style)
    {
        var copy = style.Clone();
        copy.ImagePath = null;   // the picture travels inside the file instead of as a path on this PC
        copy.Games = null;
        var contents = new Contents { Style = copy };
        if (style.ImagePath is { } image && File.Exists(image))
        {
            contents.ImageName = Path.GetFileName(image);
            contents.Image = Convert.ToBase64String(File.ReadAllBytes(image));
        }
        File.WriteAllText(path, JsonSerializer.Serialize(contents, Json));
    }

    /// <summary>
    /// Opens a crosshair file. Files come from other people, so everything is checked: size, format, ranges,
    /// and the picture must really be an image. An included picture is saved into <paramref name="imagesFolder"/>.
    /// </summary>
    /// <param name="warning">Set when the crosshair opened but its picture couldn't be used.</param>
    public static bool TryOpen(string path, string imagesFolder, out CrosshairStyle style, out string? error, out string? warning)
    {
        style = new CrosshairStyle();
        error = warning = null;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                error = "That file is too big to be a crosshair.";
                return false;
            }
            var contents = JsonSerializer.Deserialize<Contents>(File.ReadAllText(path), Json);
            if (contents?.Style is null)
            {
                error = "That isn't an OpenCrosshair file.";
                return false;
            }
            style = contents.Style;
            style.ImagePath = null;
            style.Games = null;
            if (contents.Image is not null)
            {
                var bytes = Convert.FromBase64String(contents.Image);
                Directory.CreateDirectory(imagesFolder);
                // Only the bare file name is used (no folders), so a file can't write anywhere else.
                var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
                var target = Path.Combine(imagesFolder, $"{hash}-{SafeFileName(contents.ImageName)}");
                File.WriteAllBytes(target, bytes);
                style.ImagePath = target;
                if (CrosshairRenderer.LoadImage(style) is { } check) check.Dispose();
                else
                {
                    File.Delete(target);
                    style.ImagePath = null;
                    warning = "The picture in that file couldn't be read, so the drawn crosshair is used instead.";
                }
            }
            style.Clamp();
            return true;
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = "That file couldn't be read as an OpenCrosshair crosshair.";
            return false;
        }
    }

    internal static string SafeFileName(string? name)
    {
        var bare = Path.GetFileName(name ?? "");   // drops any folder parts, "..\" included
        foreach (var bad in Path.GetInvalidFileNameChars()) bare = bare.Replace(bad, '_');
        bare = bare.Trim().TrimStart('.');
        if (bare.Length == 0) bare = "picture.png";
        return bare.Length <= 60 ? bare : bare[^60..];
    }

    public static string DefaultImagesFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCrosshair", "images");
}
