using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCrosshair;

/// <summary>One crosshair design. Edited directly in the designer's PropertyGrid.</summary>
internal sealed class CrosshairStyle
{
    [Category("General")] public string Name { get; set; } = "Default";
    [Category("General"), JsonConverter(typeof(HexColorConverter))] public Color Color { get; set; } = Color.Lime;
    [Category("General"), Description("5 to 100")] public int OpacityPercent { get; set; } = 100;

    [Category("Lines"), Description("Arm length in pixels, 0 hides the arms (0 to 100)")] public int Length { get; set; } = 6;
    [Category("Lines"), Description("1 to 30")] public int Thickness { get; set; } = 2;
    [Category("Lines"), Description("Distance from the center to each arm (0 to 100)")] public int Gap { get; set; } = 3;
    [Category("Lines"), Description("Hide the top arm")] public bool TStyle { get; set; }

    [Category("Dot"), Description("Center dot size in pixels, 0 = none (0 to 30)")] public int DotSize { get; set; }

    [Category("Circle"), Description("0 = none (0 to 200)")] public int CircleRadius { get; set; }
    [Category("Circle"), Description("1 to 20")] public int CircleThickness { get; set; } = 1;

    [Category("Outline")] public bool Outline { get; set; } = true;
    [Category("Outline"), Description("1 to 5")] public int OutlineThickness { get; set; } = 1;
    [Category("Outline"), JsonConverter(typeof(HexColorConverter))] public Color OutlineColor { get; set; } = Color.Black;

    public CrosshairStyle Clone() => (CrosshairStyle)MemberwiseClone();

    /// <summary>Forces every value into range. Settings files and share codes are untrusted input.</summary>
    public void Clamp()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Crosshair" : Name.Trim()[..Math.Min(Name.Trim().Length, 40)];
        Color = Color.FromArgb(255, Color);
        OutlineColor = Color.FromArgb(255, OutlineColor);
        OpacityPercent = Math.Clamp(OpacityPercent, 5, 100);
        Length = Math.Clamp(Length, 0, 100);
        Thickness = Math.Clamp(Thickness, 1, 30);
        Gap = Math.Clamp(Gap, 0, 100);
        DotSize = Math.Clamp(DotSize, 0, 30);
        CircleRadius = Math.Clamp(CircleRadius, 0, 200);
        CircleThickness = Math.Clamp(CircleThickness, 1, 20);
        OutlineThickness = Math.Clamp(OutlineThickness, 1, 5);
    }

    private const string Prefix = "OC1:";

    /// <summary>Short text code, e.g. OC1:00FF00,100,6,2,3,0,0,0,1,1,1,000000</summary>
    public string ToShareCode() => Prefix + string.Join(',',
        Hex(Color), OpacityPercent, Length, Thickness, Gap, TStyle ? 1 : 0, DotSize,
        CircleRadius, CircleThickness, Outline ? 1 : 0, OutlineThickness, Hex(OutlineColor));

    public static bool TryParseShareCode(string? code, out CrosshairStyle style)
    {
        style = new CrosshairStyle();
        code = code?.Trim();
        if (code is null || code.Length > 200 || !code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var p = code[Prefix.Length..].Split(',');
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

internal sealed class HexColorConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        CrosshairStyle.TryHex(reader.GetString() ?? "", out var c) ? c : throw new JsonException("Expected a #RRGGBB color.");

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue("#" + CrosshairStyle.Hex(value));
}
