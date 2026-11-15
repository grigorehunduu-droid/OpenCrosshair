using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenCrosshair.Tests;

/// <summary>
/// Translations live in src/OpenCrosshair/lang/&lt;code&gt;.json (English text → translation). These tests find every
/// translatable text in the code (T("…") calls and the [Description] tips) and check that each language has all of them.
/// Set OC_LANG_WRITE=1 and run the tests to add new texts (empty) to every file and drop the ones no longer used.
/// </summary>
[Collection("UI")]   // switches the app's language, like the theme tests switch the theme
public class LangTests
{
    private static string SourceFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src", "OpenCrosshair");
            if (File.Exists(Path.Combine(src, "OpenCrosshair.csproj"))) return src;
        }
        throw new DirectoryNotFoundException("src/OpenCrosshair not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Every English text the app can show, in the order it appears in the code.</summary>
    private static List<string> Texts()
    {
        var texts = new List<string>();
        foreach (var file in Directory.GetFiles(SourceFolder(), "*.cs").Order())
        {
            var code = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(code, @"\bT\(\s*\$"), $"{Path.GetFileName(file)}: T($\"…\") can't be translated; use T(\"… {{0}} …\", value)");
            foreach (Match m in Regex.Matches(code, @"\b(?:T|Description)\(\s*""((?:[^""\\]|\\.)*)"""))
            {
                var text = Regex.Unescape(m.Groups[1].Value);
                if (!texts.Contains(text)) texts.Add(text);
            }
        }
        return texts;
    }

    private static HashSet<string> Placeholders(string text) => Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public void EveryLanguageHasEveryText()
    {
        var texts = Texts();
        Assert.True(texts.Count > 100, $"only {texts.Count} texts found");
        Assert.Contains("Show / hide", texts);
        Assert.Contains("Softens the edges, 0 to 10 px", texts);   // a [Description] tip

        var folder = Path.Combine(SourceFolder(), "lang");
        var files = Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).Order().ToList();
        Assert.Equal(Lang.Available.Skip(1).Select(l => l.Code).Order(), files);

        foreach (var (code, _) in Lang.Available.Skip(1))
        {
            var table = Lang.Load(code);   // the copy built into the app
            Assert.True(table is not null, $"{code}.json isn't built into the app");
            var missing = texts.Where(t => string.IsNullOrWhiteSpace(table!.GetValueOrDefault(t))).ToList();
            Assert.True(missing.Count == 0, $"{code}: {missing.Count} not translated: {string.Join(" | ", missing)}");
            var unused = table!.Keys.Except(texts).ToList();
            Assert.True(unused.Count == 0, $"{code}: no longer in the app: {string.Join(" | ", unused)}");
            foreach (var (english, translated) in table)
                Assert.True(Placeholders(english).SetEquals(Placeholders(translated)),
                    $"{code}: “{translated}” needs the same {{0}}-style blanks as “{english}”");
        }
    }

    /// <summary>Not a check: with OC_LANG_WRITE=1, brings every lang file up to date with the code (new texts empty).</summary>
    [Fact]
    public void WriteLanguageFiles()
    {
        if (Environment.GetEnvironmentVariable("OC_LANG_WRITE") != "1") return;
        var texts = Texts();
        foreach (var (code, _) in Lang.Available.Skip(1))
        {
            var path = Path.Combine(SourceFolder(), "lang", code + ".json");
            var old = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))! : [];
            var updated = texts.ToDictionary(t => t, t => old.GetValueOrDefault(t, ""));
            File.WriteAllText(path, JsonSerializer.Serialize(updated, Json) + "\n");
        }
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-TW", "en")]
    [InlineData("ro-RO", "ro")]
    [InlineData("ja-JP", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("", "en")]
    public void PicksTheClosestLanguage(string culture, string expected) => Assert.Equal(expected, Lang.Resolve(culture));

    [Fact]
    public void TheWindowSpeaksTheChosenLanguage() => DesignerTests.RunSta(() =>
    {
        try
        {
            Lang.Apply("de");
            Assert.NotEqual("Basics", Lang.T("Basics"));
            Assert.Equal(Lang.T("Delete “{0}”?").Replace("{0}", "X"), Lang.T("Delete “{0}”?", "X"));
            var s = new AppSettings { Language = "de" };
            var host = new FakeHost();
            using var form = new DesignerForm(s, host);
            form.CreateControl();
            var all = Descendants(form).ToList();
            Assert.Contains(all.OfType<RadioButton>(), r => r.Text == Lang.T("Basics"));
            Assert.DoesNotContain(all.OfType<Button>(), b => b.Text == "Import");

            Assert.True(MenuTests.Item(form, "Language-de").Checked);   // View > Language
            Assert.Equal("Deutsch", MenuTests.Item(form, "Language-de").Text);
            MenuTests.Item(form, "Language-").PerformClick();
            Assert.Null(host.LanguageChosen);   // "Windows language"
            MenuTests.Item(form, "Language-fr").PerformClick();
            Assert.Equal("fr", host.LanguageChosen);
        }
        finally { Lang.Apply("en"); }
    });

    [Fact]
    public void UnknownLanguageInSettingsFallsBackToWindows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oc-lang-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "Language": "xx" }""");
            Assert.Null(AppSettings.Load(path).Language);
            File.WriteAllText(path, """{ "Language": "pl" }""");
            Assert.Equal("pl", AppSettings.Load(path).Language);
        }
        finally { File.Delete(path); }
    }

    /// <summary>Not a check: when OC_SNAPSHOT_DIR is set, saves the window in every language, to look for text that doesn't fit.</summary>
    [Fact]
    public void SaveLanguagePictures()
    {
        var dir = Environment.GetEnvironmentVariable("OC_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        DesignerTests.RunSta(() =>
        {
            try
            {
                Theme.Apply(AppTheme.Dark);
                foreach (var (code, _) in Lang.Available)
                {
                    Lang.Apply(code);
                    var s = new AppSettings { Language = code };
                    using var form = new DesignerForm(s, new FakeHost()) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
                    form.CreateControl();
                    ShowWindow(form.Handle, 4);   // SW_SHOWNOACTIVATE
                    foreach (var tab in new[] { "Basics", "Dot & circle", "Hotkeys" })
                    {
                        Descendants(form).OfType<RadioButton>().First(r => r.Text == Lang.T(tab)).Checked = true;
                        Application.DoEvents();
                        using var bmp = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                        bmp.Save(Path.Combine(dir, $"lang-{code}-{tab.Replace(" & ", "-")}.png"));
                    }
                    form.Close();
                }
            }
            finally
            {
                Lang.Apply("en");
                Theme.Apply(AppTheme.Light);
            }
        });
    }

    private static IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(Descendants).Prepend(c);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
