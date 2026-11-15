using System.Buffers.Text;
using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCrosshair;

/// <summary>One crosshair design. Edited directly in the designer's PropertyGrid.</summary>
internal sealed class CrosshairStyle
{
    [Category("General")] public string Name { get; set; } = "Default";
    [Category("General"), JsonConverter(typeof(HexColorConverter))] public Color Color { get; set; } = Color.Lime;
    [Category("General"), Description("5 to 100")] public int OpacityPercent { get; set; } = 100;
    [Category("General"), Description("Rotates the whole crosshair, 0 to 359 degrees (45 makes an X)")] public int Rotation { get; set; }
    [Category("General"), Description("Softens the edges, 0 to 10 px")] public int Blur { get; set; }

    [Category("Lines"), Description("Arm length in pixels, 0 hides the arms (0 to 100)")] public int Length { get; set; } = 6;
    [Category("Lines"), Description("1 to 30")] public int Thickness { get; set; } = 2;
    [Category("Lines"), Description("Distance from the center to each arm (0 to 100)")] public int Gap { get; set; } = 3;
    [Category("Lines"), Description("Number of arms, spread evenly (2 to 8)")] public int Arms { get; set; } = 4;
    [Category("Lines"), Description("Hide the first arm (the top one when Rotation is 0)")] public bool TStyle { get; set; }

    [Category("Dot"), Description("Center dot size in pixels, 0 = none (0 to 30)")] public int DotSize { get; set; }
    [Category("Dot")] public bool RoundDot { get; set; }

    [Category("Circle"), Description("0 = none (0 to 200)")] public int CircleRadius { get; set; }
    [Category("Circle"), Description("1 to 20")] public int CircleThickness { get; set; } = 1;
    [Category("Circle"), Description("How much of the circle to draw, 30 to 360 degrees. Less than 360 draws an arc centered at the bottom.")]
    public int CircleArc { get; set; } = 360;

    [Category("Shape"), JsonConverter(typeof(JsonStringEnumConverter<CrosshairShape>)),
     Description("Chevron: a ^ under the center. Triangle, Square: outlines around the center. Uses the line thickness.")]
    public CrosshairShape Shape { get; set; }
    [Category("Shape"), Description("4 to 100")] public int ShapeSize { get; set; } = 12;

    [Category("Label"), Description("Short text shown under the crosshair, up to 30 characters; emoji like 🎯 keep their own colors")]
    public string? LabelText { get; set; }
    [Category("Label"), Description("6 to 48")] public int LabelSize { get; set; } = 12;

    [Category("Outline")] public bool Outline { get; set; } = true;
    [Category("Outline"), Description("1 to 5")] public int OutlineThickness { get; set; } = 1;
    [Category("Outline"), JsonConverter(typeof(HexColorConverter))] public Color OutlineColor { get; set; } = Color.Black;

    [Category("Image"), Description("Picture to use instead of the drawn crosshair: PNG with transparency, or an animated GIF. Rotation, blur and opacity still apply.")]
    public string? ImagePath { get; set; }
    [Category("Image"), Description("Image size in percent (10 to 400)")] public int ImageScalePercent { get; set; } = 100;

    [Category("Animation"), JsonConverter(typeof(JsonStringEnumConverter<CrosshairAnimation>)),
     Description("Shoot: opens up when you left-click and eases back. Breathe: fades in and out. Blink: on and off. Rotate: spins. Rainbow: cycles through all colors (on images it shifts their colors, so white, gray and black parts of an image stay).")]
    public CrosshairAnimation Animation { get; set; }
    [Category("Animation"), Description("1 (slowest: a 4 s cycle, or 1.2 s to recover after a shot) to 10 (fastest)")]
    public int AnimationSpeed { get; set; } = 3;

    [Category("Games"), Description("Programs (like cs2.exe) that switch to this crosshair automatically when they come to the front")]
    public List<string>? Games { get; set; }

    public CrosshairStyle Clone()
    {
        var copy = (CrosshairStyle)MemberwiseClone();
        copy.Games = Games?.ToList();   // a real copy, so editing one crosshair's games doesn't change the other's
        return copy;
    }

    /// <summary>Forces every value into range. Settings files and share codes are untrusted input.</summary>
    public void Clamp()
    {
        Name = Shorten(Name, 40) ?? "Crosshair";
        Color = Color.FromArgb(255, Color);
        OutlineColor = Color.FromArgb(255, OutlineColor);
        OpacityPercent = Math.Clamp(OpacityPercent, 5, 100);
        Rotation = (Rotation % 360 + 360) % 360;
        Blur = Math.Clamp(Blur, 0, 10);
        Length = Math.Clamp(Length, 0, 100);
        Thickness = Math.Clamp(Thickness, 1, 30);
        Gap = Math.Clamp(Gap, 0, 100);
        Arms = Math.Clamp(Arms, 2, 8);
        DotSize = Math.Clamp(DotSize, 0, 30);
        CircleRadius = Math.Clamp(CircleRadius, 0, 200);
        CircleThickness = Math.Clamp(CircleThickness, 1, 20);
        CircleArc = Math.Clamp(CircleArc, 30, 360);
        if (!Enum.IsDefined(Shape)) Shape = CrosshairShape.None;
        ShapeSize = Math.Clamp(ShapeSize, 4, 100);
        LabelText = Shorten(LabelText, 30);
        LabelSize = Math.Clamp(LabelSize, 6, 48);
        OutlineThickness = Math.Clamp(OutlineThickness, 1, 5);
        ImagePath = string.IsNullOrWhiteSpace(ImagePath) ? null : ImagePath.Trim();
        ImageScalePercent = Math.Clamp(ImageScalePercent, 10, 400);
        if (!Enum.IsDefined(Animation)) Animation = CrosshairAnimation.None;
        AnimationSpeed = Math.Clamp(AnimationSpeed, 1, 10);
        Games = Games?.Select(g => Shorten(g, 100)?.ToLowerInvariant()).OfType<string>().Distinct().Take(20).ToList();
        if (Games is { Count: 0 }) Games = null;
    }

    private static string? Shorten(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.Length <= max) return text;
        return text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];   // never cut an emoji in half
    }

    private const string LegacyPrefix = "OC1:", Prefix = "OC2:";
    private static readonly JsonSerializerOptions ShareJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>"OC2:" + compressed JSON of every setting except the image file path.</summary>
    public string ToShareCode()
    {
        var copy = Clone();
        copy.ImagePath = null;   // a local path would leak the sharer's folder names and is useless to others
        copy.Games = null;       // which games you link is your business
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize))
            deflate.Write(JsonSerializer.SerializeToUtf8Bytes(copy, ShareJson));
        return Prefix + Base64Url.EncodeToString(packed.ToArray());
    }

    /// <summary>Reads OC2 codes and the shorter OC1 codes made by version 1.0.</summary>
    public static bool TryParseShareCode(string? code, out CrosshairStyle style)
    {
        style = new CrosshairStyle();
        code = code?.Trim();
        if (code is null || code.Length > 2000) return false;
        if (code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return TryParseJsonCode(code[Prefix.Length..], out style);
        if (!code.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        var p = code[LegacyPrefix.Length..].Split(',');
        if (p.Length != 12 || !TryHex(p[0], out var color) || !TryHex(p[11], out var outline)) return false;
        var n = new int[10];
        for (int i = 0; i < n.Length; i++)
            if (!int.TryParse(p[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        style = new CrosshairStyle
        {
            Name = "Imported", Color = color, OpacityPercent = n[0], Length = n[1], Thickness = n[2], Gap = n[3],
            TStyle = n[4] != 0, DotSize = n[5], CircleRadius = n[6], CircleThickness = n[7], Outline = n[8] != 0,
            OutlineThickness = n[9], OutlineColor = outline,
        };
        style.Clamp();
        return true;
    }

    private static bool TryParseJsonCode(string payload, out CrosshairStyle style)
    {
        style = new CrosshairStyle();
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(Base64Url.DecodeFromChars(payload)), CompressionMode.Decompress);
            var json = new byte[16 * 1024];   // a real style is well under 1 KB; the cap stops decompression bombs
            int read = deflate.ReadAtLeast(json, json.Length, throwOnEndOfStream: false);
            if (read == json.Length) return false;
            var parsed = JsonSerializer.Deserialize<CrosshairStyle>(json.AsSpan(0, read), ShareJson);
            if (parsed is null) return false;
            parsed.ImagePath = null;
            parsed.Games = null;
            parsed.Clamp();
            style = parsed;
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    internal static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    internal static bool TryHex(string text, out Color color)
    {
        color = Color.Empty;
        text = text.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int v))
            return false;
        color = Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return true;
    }
}

// Saved by name in settings and share codes: only append, never rename.
internal enum CrosshairAnimation { None, Breathe, Blink, Rotate, Rainbow, Shoot }
internal enum CrosshairShape { None, Chevron, Triangle, Square }

internal sealed class HexColorConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        CrosshairStyle.TryHex(reader.GetString() ?? "", out var c) ? c : throw new JsonException("Expected a #RRGGBB color.");

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue("#" + CrosshairStyle.Hex(value));
}
