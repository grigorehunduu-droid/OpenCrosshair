using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace OpenCrosshair;

internal sealed record Offset(int X, int Y);

/// <summary>Mouse button that hides the crosshair while held. Values are the Windows virtual-key codes.</summary>
internal enum AimButton { Right = 0x02, Middle = 0x04, Back = 0x05, Forward = 0x06 }

internal enum AppTheme { Dark, Light, System }

/// <summary>Everything the app remembers, stored as JSON in %APPDATA%\OpenCrosshair\settings.json.</summary>
internal sealed class AppSettings
{
    public List<CrosshairStyle> Profiles { get; set; } = [new CrosshairStyle()];
    public int ActiveProfile { get; set; }
    /// <summary>Screen.DeviceName of the chosen display; null = primary.</summary>
    public string? Monitor { get; set; }
    /// <summary>Pixel offset from the screen center, per display.</summary>
    public Dictionary<string, Offset> Offsets { get; set; } = new();
    public bool Visible { get; set; } = true;
    public bool HideWhileAiming { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<AimButton>))] public AimButton AimButton { get; set; } = AimButton.Right;
    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))] public AppTheme Theme { get; set; } = AppTheme.Dark;
    public Dictionary<string, string> Hotkeys { get; set; } = new(DefaultHotkeys);

    public static readonly IReadOnlyDictionary<string, string> DefaultHotkeys = new Dictionary<string, string>
    {
        ["Toggle"] = "Ctrl+Alt+H",
        ["NextProfile"] = "Ctrl+Alt+N",
        ["Designer"] = "Ctrl+Alt+D",
        ["NudgeUp"] = "Ctrl+Alt+Up",
        ["NudgeDown"] = "Ctrl+Alt+Down",
        ["NudgeLeft"] = "Ctrl+Alt+Left",
        ["NudgeRight"] = "Ctrl+Alt+Right",
        ["ResetOffset"] = "Ctrl+Alt+R",
    };

    [JsonIgnore] public CrosshairStyle Active => Profiles[ActiveProfile];

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCrosshair", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Keep the unreadable file next to the new one instead of silently losing the user's crosshairs.
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (Exception) { /* best effort */ }
            settings = new();
        }
        settings.Normalize();
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    private void Normalize()
    {
        Profiles = Profiles?.Where(p => p is not null).ToList() ?? [];
        if (Profiles.Count == 0) Profiles.Add(new CrosshairStyle());
        foreach (var p in Profiles) p.Clamp();
        ActiveProfile = Math.Clamp(ActiveProfile, 0, Profiles.Count - 1);
        Offsets ??= new();
        Hotkeys ??= new();
        if (!Enum.IsDefined(AimButton)) AimButton = AimButton.Right;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Dark;
        foreach (var (name, combo) in DefaultHotkeys) Hotkeys.TryAdd(name, combo);
    }
}

/// <summary>"Start with Windows" via the per-user Run key.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "OpenCrosshair";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
