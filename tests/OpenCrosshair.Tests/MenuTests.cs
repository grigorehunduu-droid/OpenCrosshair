namespace OpenCrosshair.Tests;

/// <summary>The customize window's menu bar, undo and redo.</summary>
[Collection("UI")]
public class MenuTests
{
    /// <summary>A menu item of the window's menu bar, found by name at any depth.</summary>
    internal static ToolStripMenuItem Item(Form form, string name) =>
        form.MainMenuStrip!.Items.Find(name, true).OfType<ToolStripMenuItem>().Single();

    [Fact]
    public void MenuBarHasTheUsualMenusWithShortcuts() => DesignerTests.RunSta(() =>
    {
        using var form = new DesignerForm(new AppSettings(), new FakeHost());
        form.CreateControl();
        var menus = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Select(m => m.Text).ToList();
        Assert.Equal(["File", "Edit", "View", "Help"], menus);
        Assert.Equal(Keys.Control | Keys.Z, Item(form, "Undo").ShortcutKeys);
        Assert.Equal(Keys.Control | Keys.N, Item(form, "FileNew").ShortcutKeys);
    });

    [Fact]
    public void ChangesAndDeletesCanBeUndoneAndRedone() => DesignerTests.RunSta(() =>
    {
        var s = new AppSettings();
        s.Profiles.Add(new CrosshairStyle { Name = "Second" });
        var host = new FakeHost();
        using var form = new DesignerForm(s, host);
        form.CreateControl();
        Assert.False(Item(form, "Undo").Enabled);   // nothing to take back yet

        var gap = form.Controls.Find(nameof(CrosshairStyle.Gap), true).OfType<NumberBox>().Single();
        int before = s.Active.Gap;
        gap.Value = before + 5;
        Assert.Equal(before + 5, s.Active.Gap);
        Assert.True(Item(form, "Undo").Enabled);

        Item(form, "FileDelete").PerformClick();      // no "are you sure?" box: it's undoable
        Assert.Single(s.Profiles);
        Assert.Equal("Second", s.Active.Name);

        form.Undo();                                  // the crosshair is back, with its change
        Assert.Equal(2, s.Profiles.Count);
        Assert.Equal("Default", s.Active.Name);
        Assert.Equal(before + 5, s.Active.Gap);
        form.Undo();                                  // and now the change is gone too
        Assert.Equal(before, s.Active.Gap);
        Assert.Equal(before, gap.Value);              // the controls show it
        Assert.False(Item(form, "Undo").Enabled);

        form.Redo();
        Assert.Equal(before + 5, s.Active.Gap);
        Assert.True(Item(form, "Redo").Enabled);
        Assert.True(host.Changes >= 5);               // every step reached the screen and was saved
    });

    [Fact]
    public void TheLastCrosshairIsKept() => DesignerTests.RunSta(() =>
    {
        var s = new AppSettings();
        using var form = new DesignerForm(s, new FakeHost());
        form.CreateControl();
        Item(form, "FileDelete").PerformClick();
        Assert.Single(s.Profiles);
        Assert.Equal("Keep at least one crosshair.", form.Controls.Find("Status", true).Single().Text);
    });
}
