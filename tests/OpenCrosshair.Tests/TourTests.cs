using System.Runtime.InteropServices;

namespace OpenCrosshair.Tests;

[Collection("UI")]
public class TourTests
{
    /// <summary>Runs the window's message loop until <paramref name="condition"/> holds (the tour animates on a timer).</summary>
    private static bool WaitFor(Func<bool> condition, double seconds = 3)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        return condition();
    }

    [Fact]
    public void TourWalksThroughWaitsForTryItAndCanBeSkipped() => DesignerTests.RunSta(() =>
    {
        var s = new AppSettings();
        var host = new FakeHost();
        // Far off-screen and never activated, so nothing pops up while the tests run.
        using var form = new DesignerForm(s, host) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.CreateControl();
        ShowWindow(form.Handle, 4);   // SW_SHOWNOACTIVATE
        form.StartTour();
        Assert.True(s.TourSeen);      // saved, so it starts by itself only once
        Assert.Equal(1, host.Changes);
        var tour = form.CurrentTour!;
        bool ended = false;
        tour.Ended += () => ended = true;
        Assert.Equal(0, tour.Step);

        tour.Next();
        tour.Next();                  // "Try it: add a ready-made one"
        Assert.Equal(2, tour.Step);
        Assert.False(WaitFor(() => tour.Step != 2, 0.3));   // waits for you
        form.Controls.Find("Presets", true).OfType<ComboBox>().Single().SelectedIndex = 1;
        Assert.True(WaitFor(() => tour.Step == 3));          // moves on by itself after a moment

        tour.Back();
        Assert.Equal(2, tour.Step);
        tour.Skip();
        Assert.True(WaitFor(() => ended));                   // fades out, then it's gone
        Assert.Equal(2, s.Profiles.Count);
        form.Close();
    });

    [Fact]
    public void CardSitsBesideTheSpotlightInsideTheWindow()
    {
        var area = new Rectangle(-6000, -6000, 1200, 700);   // off-screen, so the screen doesn't limit it
        var card = new Size(300, 200);
        Assert.Equal(new Point(-6000 + 50, -6000 + 72), Tour.CardSpot(new Rectangle(-6000 + 50, -6000 + 16, 50, 40), card, area));   // below it
        var nearRight = Tour.CardSpot(new Rectangle(-6000 + 1000, -6000 + 100, 150, 500), card, area);   // tall, at the right edge
        Assert.Equal(-6000 + 1000 - 16 - 300, nearRight.X);                                                                      // left of it
        Assert.Equal(new Point(-6000 + 450, -6000 + 250), Tour.CardSpot(new Rectangle(-6000 + 600, -6000 + 350, 0, 0), card, area)); // centered
    }

    /// <summary>Not a check: when OC_SNAPSHOT_DIR is set, saves pictures of some tour steps (window + dim layer + card).</summary>
    [Fact]
    public void SaveTourPictures()
    {
        var dir = Environment.GetEnvironmentVariable("OC_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        DesignerTests.RunSta(() =>
        {
            try
            {
                foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
                {
                    Theme.Apply(theme);
                    var s = new AppSettings();
                    using var form = new DesignerForm(s, new FakeHost()) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
                    form.CreateControl();
                    ShowWindow(form.Handle, 4);
                    form.StartTour();
                    var tour = form.CurrentTour!;
                    foreach (int step in new[] { 0, 2, 5, 6 })
                    {
                        while (tour.Step < step) tour.Next();
                        WaitFor(() => false, 0.6);   // let the spotlight arrive
                        var (shade, card) = tour.Parts;
                        using var bmp = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.DrawImageUnscaled(shade!, 0, 0);
                            using var cardBmp = new Bitmap(card.Width, card.Height);
                            card.DrawToBitmap(cardBmp, new Rectangle(Point.Empty, card.Size));
                            g.DrawImageUnscaled(cardBmp, card.Left - form.Left, card.Top - form.Top);
                        }
                        bmp.Save(Path.Combine(dir, $"tour-{theme}-{step}.png"));
                    }
                    form.Close();
                }
            }
            finally { Theme.Apply(AppTheme.Light); }
        });
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
