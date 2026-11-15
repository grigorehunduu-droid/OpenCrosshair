using System.Runtime.InteropServices;

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
    public void LabelEmojiKeepTheirOwnColors()
    {
        var style = new CrosshairStyle { Color = Color.FromArgb(0, 255, 255), LabelText = "😀", LabelSize = 24, Outline = true, OutlineThickness = 1, OutlineColor = Color.Black };
        using var bmp = CrosshairRenderer.Draw(style);
        int yellow = 0, cyan = 0, black = 0;
        for (int y = bmp.Height / 2 + 10; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.A < 200) continue;
                if (p.R > 200 && p.G > 140 && p.B < 100) yellow++;        // the face, in the emoji's own color
                if (p.R < 60 && p.G > 200 && p.B > 200) cyan++;           // not tinted in the crosshair color
                if (p.R < 40 && p.G < 40 && p.B < 40) black++;            // the outline (and the eyes)
            }
        Assert.True(yellow > 50, $"yellow {yellow}");
        Assert.True(cyan < 5, $"cyan {cyan}");
        Assert.True(black > 20, $"black {black}");

        var tooLong = new CrosshairStyle { LabelText = new string('a', 29) + "😀" };
        tooLong.Clamp();
        Assert.Equal(new string('a', 29), tooLong.LabelText);   // the emoji is dropped whole, not cut in half
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
    public void PlaysARealAnimatedGif()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        GifWriter.Write(path, 4, 4, [(Color.Red, 10), (Color.Blue, 30)]);   // delays in 1/100 s
        var loaded = CrosshairRenderer.LoadImageFrames(new CrosshairStyle { ImagePath = path });
        try
        {
            Assert.NotNull(loaded);
            var (frames, delays) = loaded.Value;
            Assert.Equal(2, frames.Count);
            Assert.Equal([100, 300], delays);
            Assert.Equal(Color.Red.ToArgb(), frames[0].GetPixel(2, 2).ToArgb());
            Assert.Equal(Color.Blue.ToArgb(), frames[1].GetPixel(2, 2).ToArgb());
            using var sprite = new CrosshairSprite(new CrosshairStyle { ImagePath = path });
            Assert.True(sprite.IsAnimated);
            Assert.False(sprite.ImageMissing);
        }
        finally
        {
            foreach (var frame in loaded?.Frames ?? []) frame.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public void BigImagesAreShrunkToFit1024Pixels()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            using (var src = new Bitmap(1500, 10)) src.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            using var bmp = CrosshairRenderer.Render(new CrosshairStyle { ImagePath = path });
            Assert.Equal(1024, bmp.Width);
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
        using var start = CrosshairAnimator.Frame(still, s, 0, drawn: true)!;
        using var quarter = CrosshairAnimator.Frame(still, s, 0.25, drawn: true)!;
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
        using var frame = CrosshairAnimator.Frame(still, s, 1 / 3.0, drawn: false)!;   // image path: hues shift, 120° turns red green
        var p = frame.GetPixel(frame.Width / 2 + 4, frame.Height / 2);
        Assert.True(p.G > p.R && p.G > p.B);
    }

    [Fact]
    public void RainbowRecolorsAWhiteDrawnCrosshair()
    {
        var s = new CrosshairStyle { Animation = CrosshairAnimation.Rainbow, Color = Color.White, Outline = false };
        using var still = CrosshairRenderer.Draw(s);
        using var frame = CrosshairAnimator.Frame(still, s, 2 / 3.0, drawn: true)!;   // 240° = blue
        Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), frame.GetPixel(frame.Width / 2 + 4, frame.Height / 2).ToArgb());
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(240, 0, 0, 255)]
    [InlineData(60, 255, 255, 0)]
    [InlineData(-120, 0, 0, 255)]
    public void HueGivesPureColors(double degrees, int r, int g, int b) =>
        Assert.Equal(Color.FromArgb(255, r, g, b).ToArgb(), CrosshairAnimator.Hue(degrees).ToArgb());

    [Fact]
    public void FrameIndexFollowsGifDelaysAndLoops()
    {
        int[] delays = [100, 300];
        Assert.Equal(0, CrosshairAnimator.FrameIndex(delays, 50));
        Assert.Equal(1, CrosshairAnimator.FrameIndex(delays, 150));
        Assert.Equal(0, CrosshairAnimator.FrameIndex(delays, 450));   // 450 ms = 50 ms into the second loop
        Assert.Equal(0, CrosshairAnimator.FrameIndex([0], 999));
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
        var json = """{ "Profiles": [ { "Animation": "Rainbow", "Shape": "Square" } ], "AimButton": "Back" }""";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, json);
            var s = AppSettings.Load(path);
            Assert.Equal(CrosshairAnimation.Rainbow, s.Active.Animation);
            Assert.Equal(CrosshairShape.Square, s.Active.Shape);
            Assert.Equal(AimButton.Back, s.AimButton);
            Assert.Equal(AimButton.Right, AppSettings.Load(path + ".missing").AimButton);   // default for new and 1.x files
        }
        finally { File.Delete(path); }
    }
}

/// <summary>
/// Writes a tiny animated GIF so the GIF path can be tested with a real file (System.Drawing can't save one).
/// 4-color palette, one solid color per frame, "uncompressed" LZW: a clear code before every 2 pixels keeps codes 3 bits wide.
/// </summary>
internal static class GifWriter
{
    public static void Write(string path, int width, int height, (Color Color, int DelayCs)[] frames)
    {
        Color[] palette = [Color.Black, Color.Red, Color.Blue, Color.Lime];
        using var f = new BinaryWriter(File.Create(path));
        f.Write("GIF89a"u8);
        f.Write((ushort)width); f.Write((ushort)height);
        f.Write((byte)0x91); f.Write((byte)0); f.Write((byte)0);   // global palette with 4 entries
        foreach (var c in palette) { f.Write(c.R); f.Write(c.G); f.Write(c.B); }
        f.Write([0x21, 0xFF, 11]); f.Write("NETSCAPE2.0"u8); f.Write([3, 1, 0, 0, 0]);   // loop forever
        foreach (var (color, delay) in frames)
        {
            byte index = (byte)Array.FindIndex(palette, p => p.ToArgb() == color.ToArgb());
            f.Write([0x21, 0xF9, 4, 0x08]); f.Write((ushort)delay); f.Write([0, 0]);   // graphic control: delay
            f.Write((byte)0x2C); f.Write((ushort)0); f.Write((ushort)0); f.Write((ushort)width); f.Write((ushort)height); f.Write((byte)0);
            f.Write((byte)2);   // LZW minimum code size; codes 4 = clear, 5 = end
            var bits = new List<bool>();
            void Code(int c) { for (int b = 0; b < 3; b++) bits.Add((c >> b & 1) == 1); }
            for (int p = 0; p < width * height; p++)
            {
                if (p % 2 == 0) Code(4);
                Code(index);
            }
            Code(5);
            var data = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) data[i / 8] |= (byte)(1 << (i % 8));
            for (int i = 0; i < data.Length; i += 255)
            {
                int n = Math.Min(255, data.Length - i);
                f.Write((byte)n);
                f.Write(data, i, n);
            }
            f.Write((byte)0);
        }
        f.Write((byte)0x3B);
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

[Collection("UI")]   // these and Version13WindowTests switch the theme, so they must not run at the same time
public class DesignerTests
{
    /// <summary>WinForms needs an STA thread; xUnit runs tests on MTA threads.</summary>
    internal static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Exception("Failed on the STA thread", failure);
    }

    private static T Find<T>(Form form, string name) where T : Control => form.Controls.Find(name, true).OfType<T>().Single();

    [Fact]
    public void SyncingFromOutsideDoesNotEchoAChange() => RunSta(() =>
    {
        var s = new AppSettings();
        var host = new FakeHost();
        using var form = new DesignerForm(s, host);
        form.CreateControl();
        s.Profiles.Add(new CrosshairStyle { Name = "Second", Gap = 9 });
        s.ActiveProfile = 1;
        form.SyncFromSettings();
        Assert.Equal(0, host.Changes);
        Assert.Equal(9, Find<NumberBox>(form, "Gap").Value);   // controls show the newly active crosshair
    });

    [Fact]
    public void ControlsChangeTheCrosshair() => RunSta(() =>
    {
        var s = new AppSettings();
        var host = new FakeHost();
        using var form = new DesignerForm(s, host);
        form.CreateControl();

        var gap = Find<NumberBox>(form, "Gap");
        Assert.Equal(3, gap.Value);
        gap.Value = 7;
        Assert.Equal(7, s.Active.Gap);
        Assert.Equal(1, host.Changes);
        Assert.Equal(7, Find<TrackBar>(form, "GapSlider").Value);   // slider and number box stay in step

        Find<TrackBar>(form, "LengthSlider").Value = 20;
        Assert.Equal(20, s.Active.Length);

        Find<CheckBox>(form, "RoundDot").Checked = true;
        Assert.True(s.Active.RoundDot);

        Find<ComboBox>(form, "Animation").SelectedIndex = 1;
        Assert.Equal(CrosshairAnimation.Shoot, s.Active.Animation);

        Find<TextBox>(form, "Name").Text = "Sniper";
        Assert.Equal("Sniper", s.Active.Name);
    });

    [Fact]
    public void PresetsAddANamedCrosshair() => RunSta(() =>
    {
        var s = new AppSettings();
        using var form = new DesignerForm(s, new FakeHost());
        form.CreateControl();
        var presets = Find<ComboBox>(form, "Presets");
        presets.SelectedIndex = presets.Items.IndexOf("Dot only");
        Assert.Equal(2, s.Profiles.Count);
        Assert.Equal("Dot only", s.Active.Name);
        Assert.Equal(0, s.Active.Length);
        Assert.Equal(0, presets.SelectedIndex);   // back to "+ Add a ready-made crosshair…"
    });

    [Fact]
    public void DarkThemeColorsTheTrayMenu()
    {
        Theme.Apply(AppTheme.Dark);
        try
        {
            var colors = Assert.IsType<ToolStripProfessionalRenderer>(ToolStripManager.Renderer).ColorTable;
            Assert.True(colors.ToolStripDropDownBackground.GetBrightness() < 0.2);
            Assert.True(Theme.MenuText.GetBrightness() > 0.8);
        }
        finally { Theme.Apply(AppTheme.Light); }
        Assert.False(Theme.IsDark);
    }

    [Fact]
    public void DarkThemeColorsTheDesignerButKeepsSwatchesAndPreview() => RunSta(() =>
    {
        Theme.Apply(AppTheme.Dark);
        try
        {
            var s = new AppSettings();
            s.Profiles[0].Color = Color.Lime;
            using var form = new DesignerForm(s, new FakeHost());
            form.CreateControl();
            Assert.True(form.BackColor.GetBrightness() < 0.2);
            Assert.True(Find<TextBox>(form, "Name").BackColor.GetBrightness() < 0.25);
            Assert.True(Find<TextBox>(form, "Name").ForeColor.GetBrightness() > 0.8);
            Assert.Equal(Color.Lime.ToArgb(), Find<Button>(form, "Color").BackColor.ToArgb());   // swatch shows the crosshair color
            Assert.Equal(0, DwmGetWindowAttribute(form.Handle, 20, out int darkTitleBar, sizeof(int)));
            Assert.Equal(1, darkTitleBar);   // Windows was asked for a dark title bar
        }
        finally { Theme.Apply(AppTheme.Light); }
    });

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    // Kept in this class so it never runs at the same time as the theme tests above (xUnit runs classes in parallel).
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);

    /// <summary>
    /// Not a check: when OC_SNAPSHOT_DIR is set, saves pictures of the designer in both themes for a visual review.
    /// The window is shown far off-screen and never activated.
    /// </summary>
    [Fact]
    public void SaveDesignerPictures()
    {
        var dir = Environment.GetEnvironmentVariable("OC_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        RunSta(() =>
        {
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                Theme.Apply(theme);
                var s = new AppSettings();
                s.Profiles[0].DotSize = 2;
                s.Profiles.Add(new CrosshairStyle { Name = "Sniper", Length = 12, Gap = 5, CircleRadius = 16 });
                using var form = new DesignerForm(s, new FakeHost()) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
                form.CreateControl();
                ShowWindow(form.Handle, 4);   // SW_SHOWNOACTIVATE
                foreach (var tab in new[] { "Basics", "Lines", "Animation" })
                {
                    Controls(form).OfType<RadioButton>().First(r => r.Text == tab).Checked = true;
                    Application.DoEvents();
                    using var bmp = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                    bmp.Save(Path.Combine(dir, $"designer-{theme}-{tab}.png"));
                }
                form.Close();
            }
            Theme.Apply(AppTheme.Light);
        });
        SavePresetGallery(Path.Combine(dir, "presets.png"));
    }

    /// <summary>The ready-made crosshairs side by side, 3× zoom, for the README.</summary>
    private static void SavePresetGallery(string path)
    {
        const int columns = 3, tileW = 200, tileH = 170, gap = 10;
        var presets = DesignerForm.Presets;
        int rows = (presets.Length + columns - 1) / columns;
        using var sheet = new Bitmap(columns * (tileW + gap) + gap, rows * (tileH + gap) + gap);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(22, 23, 26));
        using var font = new Font("Segoe UI", 10f);
        using var tileBrush = new SolidBrush(Color.FromArgb(40, 44, 52));
        for (int i = 0; i < presets.Length; i++)
        {
            int x = gap + i % columns * (tileW + gap), y = gap + i / columns * (tileH + gap);
            g.FillRectangle(tileBrush, x, y, tileW, tileH);
            using var crosshair = CrosshairRenderer.Draw(presets[i].Style);
            int zoom = Math.Min(3, 120 / crosshair.Width);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            int size = crosshair.Width * zoom;
            g.DrawImage(crosshair, x + (tileW - size) / 2, y + (135 - size) / 2, size, size);
            var label = presets[i].Name + (presets[i].Style.Animation == CrosshairAnimation.None ? "" : " (animated)");
            TextRenderer.DrawText(g, label, font, new Rectangle(x, y + 135, tileW, 30), Color.FromArgb(220, 220, 220),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        sheet.Save(path);
    }

    private static IEnumerable<Control> Controls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Controls(c).Prepend(c));
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
