namespace OpenCrosshair.Tests;

public class ShareCodeTests
{
    [Fact]
    public void RoundTrips()
    {
        var s = new CrosshairStyle
        {
            Color = Color.FromArgb(255, 0x12, 0x34, 0x56), OpacityPercent = 80, Length = 9, Thickness = 3, Gap = 4,
            TStyle = true, DotSize = 2, CircleRadius = 12, CircleThickness = 2, Outline = false, OutlineThickness = 2,
            OutlineColor = Color.FromArgb(255, 1, 2, 3),
        };
        Assert.Equal("OC1:123456,80,9,3,4,1,2,12,2,0,2,010203", s.ToShareCode());
        Assert.True(CrosshairStyle.TryParseShareCode(s.ToShareCode(), out var back));
        Assert.Equal(s.ToShareCode(), back.ToShareCode());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OC1:")]
    [InlineData("OC2:00FF00,100,6,2,3,0,0,0,1,1,1,000000")]   // unknown version
    [InlineData("OC1:zzzzzz,100,6,2,3,0,0,0,1,1,1,000000")]   // bad color
    [InlineData("OC1:00FF00,-5,6,2,3,0,0,0,1,1,1,000000")]    // negative number
    [InlineData("OC1:00FF00,100,6,2,3,0,0,0,1,1,000000")]     // missing field
    public void RejectsInvalidCodes(string? code) => Assert.False(CrosshairStyle.TryParseShareCode(code, out _));

    [Fact]
    public void ClampsOutOfRangeValues()
    {
        Assert.True(CrosshairStyle.TryParseShareCode("OC1:00FF00,1,9999,0,3,0,0,0,1,9,1,000000", out var s));
        Assert.Equal(5, s.OpacityPercent);
        Assert.Equal(100, s.Length);
        Assert.Equal(1, s.Thickness);
        Assert.Equal(1, s.CircleThickness);
    }
}

public class RendererTests
{
    [Fact]
    public void DrawsArmsAroundTheCenterWithAGap()
    {
        var s = new CrosshairStyle { Length = 4, Thickness = 2, Gap = 3, Outline = false, Color = Color.Red };
        using var bmp = CrosshairRenderer.Render(s);
        int c = bmp.Width / 2;
        int red = Color.Red.ToArgb();
        Assert.Equal(red, bmp.GetPixel(c + 3, c).ToArgb());   // right arm starts after the gap
        Assert.Equal(red, bmp.GetPixel(c + 6, c).ToArgb());   // ...and is 4 px long
        Assert.Equal(0, bmp.GetPixel(c + 7, c).A);
        Assert.Equal(0, bmp.GetPixel(c + 2, c).A);            // gap is transparent
        Assert.Equal(red, bmp.GetPixel(c - 4, c).ToArgb());   // left arm
        Assert.Equal(red, bmp.GetPixel(c, c - 4).ToArgb());   // top arm
        Assert.Equal(red, bmp.GetPixel(c, c + 3).ToArgb());   // bottom arm
    }

    [Fact]
    public void TStyleRemovesTheTopArm()
    {
        using var bmp = CrosshairRenderer.Render(new CrosshairStyle { Length = 4, Gap = 3, Outline = false, TStyle = true });
        int c = bmp.Width / 2;
        Assert.Equal(0, bmp.GetPixel(c, c - 4).A);
        Assert.NotEqual(0, bmp.GetPixel(c, c + 3).A);
    }

    [Fact]
    public void OutlineSurroundsTheArm()
    {
        var s = new CrosshairStyle { Length = 4, Thickness = 2, Gap = 3, Outline = true, OutlineThickness = 1, OutlineColor = Color.Black };
        using var bmp = CrosshairRenderer.Render(s);
        int c = bmp.Width / 2;
        Assert.Equal(Color.Black.ToArgb(), bmp.GetPixel(c + 2, c).ToArgb());   // one outline pixel before the right arm
        Assert.Equal(Color.Lime.ToArgb(), bmp.GetPixel(c + 3, c).ToArgb());
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", 3u, Keys.H)]
    [InlineData("shift + F8", 4u, Keys.F8)]
    [InlineData("Ctrl+Alt+Up", 3u, Keys.Up)]
    [InlineData("Win+Ctrl+Right", 10u, Keys.Right)]
    public void Parses(string text, uint mods, Keys key)
    {
        Assert.True(HotkeyManager.TryParse(text, out var m, out var k));
        Assert.Equal(mods, m);
        Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+Control")]
    [InlineData("Hyper+H")]
    [InlineData("Ctrl+65")]
    public void RejectsInvalid(string text) => Assert.False(HotkeyManager.TryParse(text, out _, out _));
}

public class DesignerTests
{
    [Fact]
    public void OpensAndEditsWithoutErrors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var s = new AppSettings();
                int changes = 0;
                using var form = new DesignerForm(s, () => changes++);
                form.CreateControl();
                s.Profiles.Add(new CrosshairStyle { Name = "Second" });
                s.ActiveProfile = 1;
                form.SyncFromSettings();
                Assert.Equal(0, changes);   // syncing from outside must not echo a change back
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);   // WinForms needs an STA thread
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }
}

public class SettingsTests
{
    [Fact]
    public void SaveAndLoadKeepEverything()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var s = AppSettings.Load(path);
            s.Profiles.Add(new CrosshairStyle { Name = "Big", Length = 20, Color = Color.Cyan });
            s.ActiveProfile = 1;
            s.Offsets[@"\\.\DISPLAY2"] = new Offset(3, -2);
            s.Hotkeys["Toggle"] = "Shift+F1";
            s.Save(path);

            var back = AppSettings.Load(path);
            Assert.Equal(2, back.Profiles.Count);
            Assert.Equal(s.Active.ToShareCode(), back.Active.ToShareCode());
            Assert.Equal(new Offset(3, -2), back.Offsets[@"\\.\DISPLAY2"]);
            Assert.Equal("Shift+F1", back.Hotkeys["Toggle"]);
            Assert.Equal("Ctrl+Alt+N", back.Hotkeys["NextProfile"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CorruptFileIsKeptAndDefaultsAreUsed()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{ not json");
            var s = AppSettings.Load(path);
            Assert.Single(s.Profiles);
            Assert.Equal("{ not json", File.ReadAllText(path + ".bad"));
        }
        finally { File.Delete(path); File.Delete(path + ".bad"); }
    }

    [Fact]
    public void OutOfRangeValuesInTheFileAreClamped()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{ "Profiles": [ { "Thickness": 500, "Color": "#FF0000" } ], "ActiveProfile": 7 }""");
            var s = AppSettings.Load(path);
            Assert.Equal(30, s.Active.Thickness);
            Assert.Equal(0, s.ActiveProfile);
            Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), s.Active.Color.ToArgb());
        }
        finally { File.Delete(path); }
    }
}
