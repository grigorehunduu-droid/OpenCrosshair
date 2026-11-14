namespace OpenCrosshair.Tests;

public class ShareCodeTests
{
    [Fact]
    public void RoundTripsEverySetting()
    {
        var s = new CrosshairStyle
        {
            Name = "Mine", Color = Color.FromArgb(255, 0x12, 0x34, 0x56), OpacityPercent = 80, Rotation = 45, Blur = 2,
            Length = 9, Thickness = 3, Gap = 4, Arms = 6, TStyle = true, DotSize = 2, RoundDot = true,
            CircleRadius = 12, CircleThickness = 2, CircleArc = 180, Shape = CrosshairShape.Chevron, ShapeSize = 20,
            LabelText = "AWP", LabelSize = 14, Outline = false, OutlineThickness = 2, OutlineColor = Color.FromArgb(255, 1, 2, 3),
            ImageScalePercent = 150, Animation = CrosshairAnimation.Shoot, AnimationSpeed = 7,
        };
        Assert.StartsWith("OC2:", s.ToShareCode());
        Assert.True(CrosshairStyle.TryParseShareCode(s.ToShareCode(), out var back));
        Assert.Equal(s.ToShareCode(), back.ToShareCode());
        Assert.Equal("Mine", back.Name);
        Assert.Equal(CrosshairShape.Chevron, back.Shape);
        Assert.Equal(CrosshairAnimation.Shoot, back.Animation);
    }

    [Fact]
    public void LeavesOutTheImagePath()
    {
        var s = new CrosshairStyle { ImagePath = @"C:\Users\Someone\Pictures\scope.png" };
        Assert.True(CrosshairStyle.TryParseShareCode(s.ToShareCode(), out var back));
        Assert.Null(back.ImagePath);
        Assert.Equal(@"C:\Users\Someone\Pictures\scope.png", s.ImagePath);   // the original isn't touched
    }

    [Fact]
    public void ReadsVersion1Codes()
    {
        Assert.True(CrosshairStyle.TryParseShareCode("OC1:123456,80,9,3,4,1,2,12,2,0,2,010203", out var s));
        Assert.Equal(Color.FromArgb(255, 0x12, 0x34, 0x56).ToArgb(), s.Color.ToArgb());
        Assert.Equal((80, 9, 3, 4, true, 2, 12), (s.OpacityPercent, s.Length, s.Thickness, s.Gap, s.TStyle, s.DotSize, s.CircleRadius));
        Assert.False(s.Outline);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OC1:")]
    [InlineData("OC3:abc")]                                     // unknown version
    [InlineData("OC2:00FF00,100,6,2,3,0,0,0,1,1,1,000000")]    // not base64url
    [InlineData("OC2:aGVsbG8")]                                 // base64 but not deflate/JSON
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

public class DesignerOptionTests
{
    private static int Alpha(Bitmap b, int dx, int dy) => b.GetPixel(b.Width / 2 + dx, b.Height / 2 + dy).A;

    [Fact]
    public void ThreeArmsLeaveNoArmPointingDown()
    {
        using var bmp = CrosshairRenderer.Draw(new CrosshairStyle { Arms = 3, Length = 8, Gap = 3, Outline = false });
        Assert.True(Alpha(bmp, 0, -7) > 200);   // first arm points up
        Assert.Equal(0, Alpha(bmp, 0, 7));      // arms at 30° and 150°, none straight down
    }

    [Fact]
    public void RotationTurnsAPlusIntoAnX()
    {
        using var bmp = CrosshairRenderer.Draw(new CrosshairStyle { Rotation = 45, Length = 10, Gap = 3, Outline = false });
        Assert.True(Alpha(bmp, 6, 6) > 100);    // on the diagonal
        Assert.Equal(0, Alpha(bmp, 8, 0));      // nothing on the horizontal any more
    }

    [Fact]
    public void RoundDotHasNoCorners()
    {
        using var round = CrosshairRenderer.Draw(new CrosshairStyle { Length = 0, DotSize = 12, RoundDot = true, Outline = false });
        using var square = CrosshairRenderer.Draw(new CrosshairStyle { Length = 0, DotSize = 12, Outline = false });
        Assert.Equal(0, Alpha(round, -6, -6));
        Assert.Equal(255, Alpha(square, -6, -6));
    }

    [Fact]
    public void ArcOnlyDrawsTheBottomPart()
    {
        using var bmp = CrosshairRenderer.Draw(new CrosshairStyle { Length = 0, CircleRadius = 20, CircleThickness = 2, CircleArc = 90, Outline = false });
        Assert.True(Alpha(bmp, 0, 20) > 100);   // bottom of the circle
        Assert.Equal(0, Alpha(bmp, 0, -21));    // top is left out
    }

    [Fact]
    public void ChevronSitsUnderTheCenter()
    {
        using var bmp = CrosshairRenderer.Draw(new CrosshairStyle { Length = 0, Shape = CrosshairShape.Chevron, ShapeSize = 20, Gap = 4, Outline = false });
        Assert.True(Alpha(bmp, 0, 4) > 100);    // tip, one gap below the center
        Assert.True(Alpha(bmp, 7, 11) > 100);   // along the right leg
        Assert.Equal(0, Alpha(bmp, 0, -6));
    }

    [Fact]
    public void LabelIsDrawnUnderTheCrosshair()
    {
        using var plain = CrosshairRenderer.Draw(new CrosshairStyle());
        using var labeled = CrosshairRenderer.Draw(new CrosshairStyle { LabelText = "HELLO" });
        Assert.True(labeled.Width > plain.Width);
        int c = labeled.Width / 2, solid = 0;
        for (int y = c + 12; y < labeled.Height; y++)
            for (int x = 0; x < labeled.Width; x++)
                if (labeled.GetPixel(x, y).A > 200) solid++;
        Assert.True(solid > 20);
    }

    [Fact]
    public void BlurSoftensAndPads()
    {
        using var sharp = CrosshairRenderer.Draw(new CrosshairStyle { Outline = false });
        using var soft = CrosshairRenderer.Draw(new CrosshairStyle { Outline = false, Blur = 3 });
        Assert.Equal(sharp.Width + 12, soft.Width);   // 2 × radius of room on each side
        Assert.Equal(0, soft.Width % 2);
        Assert.InRange(Alpha(soft, 3, -1), 1, 254);   // the arm's hard edge is now partly transparent
    }
}

public class ImageTests
{
    [Fact]
    public void LoadsAndScalesAnImage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            using (var src = new Bitmap(10, 6))
            {
                using (var g = Graphics.FromImage(src)) g.Clear(Color.Blue);
                src.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            using var bmp = CrosshairRenderer.Render(new CrosshairStyle { ImagePath = path, ImageScalePercent = 200 });
            Assert.Equal(20, bmp.Width);   // 20 × 12 image centered on an even, square canvas
            Assert.Equal(Color.Blue.ToArgb(), bmp.GetPixel(10, 10).ToArgb());
            Assert.Equal(0, bmp.GetPixel(10, 1).A);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingImageFallsBackToTheDrawnCrosshair()
    {
        var s = new CrosshairStyle { ImagePath = @"C:\does\not\exist.png", Outline = false };
        Assert.Null(CrosshairRenderer.LoadImage(s));
        using var bmp = CrosshairRenderer.Render(s);
        Assert.Equal(Color.Lime.ToArgb(), bmp.GetPixel(bmp.Width / 2 + 3, bmp.Height / 2).ToArgb());
    }

    [Fact]
    public void RejectsAFileThatIsNotAnImage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            File.WriteAllText(path, "not an image");
            Assert.Null(CrosshairRenderer.LoadImage(new CrosshairStyle { ImagePath = path }));
        }
        finally { File.Delete(path); }
    }
}

public class AnimationTests
{
    [Fact]
    public void BreatheAndBlinkChangeOpacity()
    {
        var breathe = new CrosshairStyle { Animation = CrosshairAnimation.Breathe };
        Assert.Equal(1, CrosshairAnimator.Opacity(breathe, 0), 3);
        Assert.Equal(0.35, CrosshairAnimator.Opacity(breathe, 0.5), 3);
        var blink = new CrosshairStyle { Animation = CrosshairAnimation.Blink };
        Assert.Equal(1, CrosshairAnimator.Opacity(blink, 0.25));
        Assert.Equal(0, CrosshairAnimator.Opacity(blink, 0.75));
        Assert.Equal(1, CrosshairAnimator.Opacity(new CrosshairStyle(), 0.75));
    }

    [Fact]
    public void RotateKeepsOneFrameSizeAndTurnsTheCrosshair()
    {
        var s = new CrosshairStyle { Animation = CrosshairAnimation.Rotate, TStyle = true, Length = 8, Gap = 3, Outline = false };
        using var still = CrosshairRenderer.Draw(s);
        using var start = CrosshairAnimator.Frame(still, s, 0)!;
        using var quarter = CrosshairAnimator.Frame(still, s, 0.25)!;
        Assert.Equal(start.Size, quarter.Size);
        int c = quarter.Width / 2;
        Assert.Equal(0, start.GetPixel(c, c - 7).A);      // T-style: no top arm
        Assert.Equal(0, quarter.GetPixel(c + 7, c).A);    // a quarter turn later the gap points right
        Assert.True(quarter.GetPixel(c, c - 7).A > 200);
    }

    [Fact]
    public void RainbowShiftsTheHue()
    {
        var s = new CrosshairStyle { Animation = CrosshairAnimation.Rainbow, Color = Color.Red, Outline = false };
        using var still = CrosshairRenderer.Draw(s);
        using var frame = CrosshairAnimator.Frame(still, s, 1 / 3.0)!;   // 120°: red becomes green
        var p = frame.GetPixel(frame.Width / 2 + 4, frame.Height / 2);
        Assert.True(p.G > p.R && p.G > p.B);
    }

    [Fact]
    public void ShootOpensTheGapThenSettles()
    {
        var s = new CrosshairStyle { Animation = CrosshairAnimation.Shoot, AnimationSpeed = 3, Length = 6, Gap = 3, Outline = false };
        Assert.Equal(1, CrosshairAnimator.Spread(s, 0), 3);
        Assert.Equal(0, CrosshairAnimator.Spread(s, 0.4));                     // 1.2 s / speed 3 = settled
        Assert.Equal(0, CrosshairAnimator.Spread(s, double.PositiveInfinity)); // never shot
        using var still = CrosshairRenderer.Draw(s);
        Assert.Null(CrosshairAnimator.Shoot(still, s, 0, drawn: true));
        using var open = CrosshairAnimator.Shoot(still, s, 1, drawn: true)!;
        int c = open.Width / 2;
        Assert.Equal(0, open.GetPixel(c + 4, c).A);         // the gap grew past where the arm started
        Assert.True(open.GetPixel(c + 10, c).A > 200);      // gap 3 + 6 extra
    }

    [Fact]
    public void SettingsReadEnumsByName()
    {
        // Settings store enums by name; adding values must not break existing files.
        var json = """{ "Profiles": [ { "Animation": "Rainbow", "Shape": "Square" } ] }""";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, json);
            var s = AppSettings.Load(path);
            Assert.Equal(CrosshairAnimation.Rainbow, s.Active.Animation);
            Assert.Equal(CrosshairShape.Square, s.Active.Shape);
        }
        finally { File.Delete(path); }
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
