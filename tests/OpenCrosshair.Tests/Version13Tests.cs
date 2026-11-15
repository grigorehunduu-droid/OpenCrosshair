namespace OpenCrosshair.Tests;

/// <summary>Stands in for the running app when testing the customize window.</summary>
internal sealed class FakeHost : IDesignerHost
{
    public int Changes, HotkeyApplies;
    public AppTheme? ThemeChosen;
    public bool? Paused;
    public void Changed() => Changes++;
    public void SetTheme(AppTheme theme) => ThemeChosen = theme;
    public string? LanguageChosen = "unset";
    public void SetLanguage(string? code) => LanguageChosen = code;
    public IReadOnlyList<string> ApplyHotkeys()
    {
        HotkeyApplies++;
        return [];
    }
    public void PauseHotkeys(bool paused) => Paused = paused;
}

public class CrosshairFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oc-tests-" + Guid.NewGuid());

    public CrosshairFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakePng(string name, Color color)
    {
        var path = Path.Combine(_dir, name);
        using var bmp = new Bitmap(6, 6);
        using (var g = Graphics.FromImage(bmp)) g.Clear(color);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public void CarriesTheCrosshairAndItsPicture()
    {
        var style = new CrosshairStyle { Name = "Scope", Gap = 9, ImagePath = MakePng("scope.png", Color.Blue), Games = ["cs2.exe"] };
        var file = Path.Combine(_dir, "scope.ochair");
        CrosshairFile.Save(file, style);

        var images = Path.Combine(_dir, "images");
        Assert.True(CrosshairFile.TryOpen(file, images, out var opened, out var error, out var warning));
        Assert.Null(error);
        Assert.Null(warning);
        Assert.Equal("Scope", opened.Name);
        Assert.Equal(9, opened.Gap);
        Assert.Null(opened.Games);                                        // your game links stay on your PC
        Assert.StartsWith(images, opened.ImagePath);                      // the picture now lives in the app's folder
        using var bmp = CrosshairRenderer.Render(opened);
        Assert.Equal(Color.Blue.ToArgb(), bmp.GetPixel(bmp.Width / 2, bmp.Height / 2).ToArgb());
    }

    [Fact]
    public void APictureNameCannotEscapeTheImagesFolder()
    {
        var file = Path.Combine(_dir, "evil.ochair");
        var png = Convert.ToBase64String(File.ReadAllBytes(MakePng("x.png", Color.Red)));
        File.WriteAllText(file, $$"""{ "Style": { "Name": "Evil" }, "ImageName": "..\\..\\..\\Windows\\evil.png", "Image": "{{png}}" }""");

        var images = Path.Combine(_dir, "images");
        Assert.True(CrosshairFile.TryOpen(file, images, out var opened, out _, out _));
        Assert.Equal(images, Path.GetDirectoryName(opened.ImagePath));
        Assert.EndsWith("evil.png", opened.ImagePath);
    }

    [Theory]
    [InlineData("""not json""")]
    [InlineData("""{ "Version": 1 }""")]
    [InlineData("""{ "Style": { "Name": "X" }, "Image": "%%% not base64 %%%" }""")]
    public void RejectsBrokenFiles(string contents)
    {
        var file = Path.Combine(_dir, "broken.ochair");
        File.WriteAllText(file, contents);
        Assert.False(CrosshairFile.TryOpen(file, Path.Combine(_dir, "images"), out _, out var error, out _));
        Assert.NotNull(error);
    }

    [Fact]
    public void APictureThatIsNotAnImageIsDroppedWithAWarning()
    {
        var file = Path.Combine(_dir, "fake.ochair");
        var notAnImage = Convert.ToBase64String("just text"u8.ToArray());
        File.WriteAllText(file, $$"""{ "Style": { "Name": "Fake", "Thickness": 999 }, "ImageName": "a.png", "Image": "{{notAnImage}}" }""");
        var images = Path.Combine(_dir, "images");
        Assert.True(CrosshairFile.TryOpen(file, images, out var opened, out _, out var warning));
        Assert.NotNull(warning);
        Assert.Null(opened.ImagePath);
        Assert.Empty(Directory.GetFiles(images));   // nothing left behind
        Assert.Equal(30, opened.Thickness);         // values are clamped like everything else
    }

    [Theory]
    [InlineData(@"..\..\x.png", "x.png")]
    [InlineData("", "picture.png")]
    [InlineData("...", "picture.png")]
    [InlineData("a<b>.png", "a_b_.png")]
    public void SafeFileNames(string input, string expected) => Assert.Equal(expected, CrosshairFile.SafeFileName(input));
}

public class GameLinkTests
{
    [Fact]
    public void FindsTheCrosshairLinkedToAGame()
    {
        var s = new AppSettings();
        s.Profiles.Add(new CrosshairStyle { Name = "CS", Games = ["cs2.exe"] });
        Assert.Equal(1, s.ProfileForGame("CS2.exe"));
        Assert.Equal(-1, s.ProfileForGame("notepad.exe"));
    }

    [Fact]
    public void CopiesDoNotShareTheirGameList()
    {
        var original = new CrosshairStyle { Games = ["a.exe"] };
        var copy = original.Clone();
        copy.Games!.Add("b.exe");
        Assert.Equal(["a.exe"], original.Games);
    }

    [Fact]
    public void GameListsAreTidiedAndKeptOutOfShareCodes()
    {
        var s = new CrosshairStyle { Games = ["CS2.exe", "cs2.exe", "  ", "Valorant.exe"] };
        s.Clamp();
        Assert.Equal(["cs2.exe", "valorant.exe"], s.Games);
        Assert.True(CrosshairStyle.TryParseShareCode(s.ToShareCode(), out var shared));
        Assert.Null(shared.Games);
    }

    [Fact]
    public void GamesAreSavedWithTheCrosshair()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var s = AppSettings.Load(path);
            s.Active.Games = ["cs2.exe"];
            s.Save(path);
            Assert.Equal(0, AppSettings.Load(path).ProfileForGame("cs2.exe"));
        }
        finally { File.Delete(path); }
    }
}

[Collection("UI")]
public class Version13WindowTests
{
    private static T Find<T>(Form form, string name) where T : Control => form.Controls.Find(name, true).OfType<T>().Single();

    [Fact]
    public void HotkeysTabShowsAndResetsHotkeys() => DesignerTests.RunSta(() =>
    {
        var s = new AppSettings();
        s.Hotkeys["Toggle"] = "Shift+F1";
        var host = new FakeHost();
        using var form = new DesignerForm(s, host);
        form.CreateControl();
        Assert.Equal("Shift+F1", Find<TextBox>(form, "HotkeyToggle").Text);
        var reset = form.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>().Single(b => b.Text == "Back to the defaults");
        // PerformClick ignores buttons that aren't on screen, and this window is never shown; raise the click directly.
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(reset, [EventArgs.Empty]);
        Assert.Equal("Ctrl+Alt+H", s.Hotkeys["Toggle"]);
        Assert.Equal("Ctrl+Alt+H", Find<TextBox>(form, "HotkeyToggle").Text);
        Assert.Equal(1, host.HotkeyApplies);
    });

    [Fact]
    public void ThemeChoiceGoesToTheApp() => DesignerTests.RunSta(() =>
    {
        var host = new FakeHost();
        using var form = new DesignerForm(new AppSettings(), host);
        form.CreateControl();
        Assert.True(MenuTests.Item(form, "ThemeDark").Checked);
        MenuTests.Item(form, "ThemeLight").PerformClick();   // View > Theme > Light
        Assert.Equal(AppTheme.Light, host.ThemeChosen);
    });

    [Fact]
    public void GamePickerOpensWithoutListingItself() => DesignerTests.RunSta(() =>
    {
        using var picker = new GamePicker();
        picker.CreateControl();
        Assert.Null(picker.Chosen);
        var list = picker.Controls.OfType<ListBox>().Single();
        Assert.DoesNotContain(list.Items.Cast<string>(), item => item.Contains("(testhost.exe)", StringComparison.OrdinalIgnoreCase));
    });

    [Fact]
    public void DarkThemeDrawsTooltipsItself()
    {
        Theme.Apply(AppTheme.Dark);
        try
        {
            using var tips = new ToolTip();
            Theme.Style(tips);
            Assert.True(tips.OwnerDraw);
        }
        finally { Theme.Apply(AppTheme.Light); }
    }

    [Fact]
    public void TheAppIconHasSmallAndLargeSizes()
    {
        using var large = AppIcon.Large;
        using var small = AppIcon.Small;
        Assert.Equal(SystemInformation.SmallIconSize, small.Size);
        Assert.True(large.Width >= 32);
    }

    private static IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(Descendants).Prepend(c);
}

public class NumberBoxTests
{
    [Fact]
    public void TypedNumbersApplyOnlyWhenInRange()
    {
        using var box = new NumberBox { Minimum = 5, Maximum = 100 };
        int changes = 0;
        box.ValueChanged += (_, _) => changes++;
        box.Value = 40;
        Assert.Equal((40, "40", 1), (box.Value, box.Text, changes));
        box.Text = "250";                 // out of range: ignored until fixed
        Assert.Equal((40, 1), (box.Value, changes));
        box.Text = "75";
        Assert.Equal((75, 2), (box.Value, changes));
        box.Value = 1000;                 // set from code: clamped
        Assert.Equal((100, "100"), (box.Value, box.Text));
    }
}
