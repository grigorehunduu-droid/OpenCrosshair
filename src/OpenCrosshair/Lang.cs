using System.Globalization;
using System.Text.Json;

namespace OpenCrosshair;

/// <summary>
/// The app's text in other languages. Every visible English text goes through T(); its translation comes from
/// lang/&lt;code&gt;.json (English text → translation), which is built into the exe. Anything missing shows in English.
/// </summary>
internal static class Lang
{
    /// <summary>Languages with a translation file, as (code, name in that language).</summary>
    public static readonly (string Code, string Name)[] Available =
    [
        ("en", "English"), ("de", "Deutsch"), ("es", "Español"), ("fr", "Français"), ("pl", "Polski"),
        ("pt-BR", "Português (Brasil)"), ("ro", "Română"), ("ru", "Русский"), ("tr", "Türkçe"), ("zh-CN", "简体中文"),
    ];

    private static IReadOnlyDictionary<string, string> _table = new Dictionary<string, string>();

    /// <summary>The language in use, e.g. "de".</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>Switches the language; null means Windows' display language if there's a translation for it, else English.</summary>
    public static void Apply(string? code)
    {
        Current = Resolve(code ?? CultureInfo.CurrentUICulture.Name);
        _table = Load(Current) ?? new Dictionary<string, string>();
    }

    /// <summary>The closest available language: the same one ("pt-BR"), else the same base language ("de-AT" → "de"), else English.</summary>
    internal static string Resolve(string culture)
    {
        foreach (var (code, _) in Available)
            if (code.Equals(culture, StringComparison.OrdinalIgnoreCase)) return code;
        var baseName = culture.Split('-')[0];
        // Traditional Chinese readers get English rather than the Simplified translation.
        if (baseName == "zh" && !(culture.Contains("CN") || culture.Contains("SG") || culture.Contains("Hans"))) return "en";
        foreach (var (code, _) in Available)
            if (code.Split('-')[0].Equals(baseName, StringComparison.OrdinalIgnoreCase)) return code;
        return "en";
    }

    internal static Dictionary<string, string>? Load(string code)
    {
        using var stream = typeof(Lang).Assembly.GetManifestResourceStream($"OpenCrosshair.lang.{code}.json");
        return stream is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }

    /// <summary>The text in the current language.</summary>
    public static string T(string english) => _table.TryGetValue(english, out var t) && t.Length > 0 ? t : english;

    /// <summary>The text in the current language, with {0}, {1}… filled in.</summary>
    public static string T(string english, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);
}
