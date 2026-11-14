using Microsoft.Win32;

namespace OpenCrosshair;

/// <summary>The running app: tray icon, overlay, hotkeys, timers. Lives until Exit.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly string _path = AppSettings.DefaultPath;
    private readonly AppSettings _s;
    private readonly OverlayWindow _overlay = new();
    private readonly NotifyIcon _tray;
    private readonly HotkeyManager _hotkeys = new();
    private readonly System.Windows.Forms.Timer _onTopTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _aimTimer = new() { Interval = 15 };
    private DesignerForm? _designer;
    private bool _aiming;

    public TrayApp()
    {
        _s = AppSettings.Load(_path);
        _tray = new NotifyIcon { Text = "OpenCrosshair", Icon = MakeTrayIcon(), Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _tray.ContextMenuStrip.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };
        _tray.DoubleClick += (_, _) => OpenDesigner();
        BuildMenu();

        _overlay.Show();
        Redraw();
        ApplyVisibility();

        _onTopTimer.Tick += (_, _) => _overlay.KeepOnTop();
        _onTopTimer.Start();
        _aimTimer.Tick += (_, _) => CheckAiming();
        _aimTimer.Enabled = _s.HideWhileAiming;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        RegisterHotkeys();
    }

    private Screen SelectedScreen() =>
        Screen.AllScreens.FirstOrDefault(x => x.DeviceName == _s.Monitor) ?? Screen.PrimaryScreen!;

    private void Redraw()
    {
        var screen = SelectedScreen();
        var off = _s.Offsets.GetValueOrDefault(screen.DeviceName) ?? new Offset(0, 0);
        byte alpha = (byte)(_s.Active.OpacityPercent * 255 / 100);
        _overlay.SetImage(CrosshairRenderer.Render(_s.Active), alpha, screen.Bounds, new Point(off.X, off.Y));
    }

    private void Save()
    {
        try { _s.Save(_path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _tray.ShowBalloonTip(4000, "OpenCrosshair", "Couldn't save settings: " + e.Message, ToolTipIcon.Warning);
        }
    }

    private void Changed()
    {
        Redraw();
        Save();
    }

    private void ApplyVisibility() => _overlay.Visible = _s.Visible && !_aiming;

    private void ToggleVisible()
    {
        _s.Visible = !_s.Visible;
        ApplyVisibility();
        Save();
    }

    private void CheckAiming()
    {
        bool held = (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0;
        if (held == _aiming) return;
        _aiming = held;
        ApplyVisibility();
    }

    private void SetHideWhileAiming(bool on)
    {
        _s.HideWhileAiming = on;
        _aimTimer.Enabled = on;
        _aiming = false;
        ApplyVisibility();
        Save();
    }

    private void SelectProfile(int index)
    {
        _s.ActiveProfile = index;
        Changed();
        _designer?.SyncFromSettings();
    }

    private void Nudge(int dx, int dy, bool reset = false)
    {
        var name = SelectedScreen().DeviceName;
        var o = _s.Offsets.GetValueOrDefault(name) ?? new Offset(0, 0);
        _s.Offsets[name] = reset ? new Offset(0, 0) : new Offset(o.X + dx, o.Y + dy);
        Changed();
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var actions = new Dictionary<string, (Action Run, bool Repeat)>
        {
            ["Toggle"] = (ToggleVisible, false),
            ["NextProfile"] = (() => SelectProfile((_s.ActiveProfile + 1) % _s.Profiles.Count), false),
            ["Designer"] = (OpenDesigner, false),
            ["NudgeUp"] = (() => Nudge(0, -1), true),
            ["NudgeDown"] = (() => Nudge(0, 1), true),
            ["NudgeLeft"] = (() => Nudge(-1, 0), true),
            ["NudgeRight"] = (() => Nudge(1, 0), true),
            ["ResetOffset"] = (() => Nudge(0, 0, reset: true), false),
        };
        var failed = actions.Where(a => !_hotkeys.Register(_s.Hotkeys[a.Key], a.Value.Run, a.Value.Repeat))
            .Select(a => $"{a.Key} ({_s.Hotkeys[a.Key]})").ToList();
        if (failed.Count > 0)
            _tray.ShowBalloonTip(6000, "OpenCrosshair",
                "These hotkeys are invalid or used by another app: " + string.Join(", ", failed), ToolTipIcon.Warning);
    }

    private void BuildMenu()
    {
        var m = _tray.ContextMenuStrip!;
        m.Items.Clear();
        m.Items.Add(Item("Show crosshair", _s.Visible, ToggleVisible));

        var profiles = new ToolStripMenuItem("Crosshair");
        for (int i = 0; i < _s.Profiles.Count; i++)
        {
            int index = i;
            profiles.DropDownItems.Add(Item(_s.Profiles[i].Name, i == _s.ActiveProfile, () => SelectProfile(index)));
        }
        m.Items.Add(profiles);

        var displays = new ToolStripMenuItem("Display");
        var current = SelectedScreen().DeviceName;
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var scr = screens[i];
            var label = $"Display {i + 1} ({scr.Bounds.Width}×{scr.Bounds.Height}{(scr.Primary ? ", primary" : "")})";
            displays.DropDownItems.Add(Item(label, scr.DeviceName == current, () => { _s.Monitor = scr.DeviceName; Changed(); }));
        }
        m.Items.Add(displays);

        m.Items.Add(Item("Hide while aiming (right mouse)", _s.HideWhileAiming, () => SetHideWhileAiming(!_s.HideWhileAiming)));
        m.Items.Add(Item("Start with Windows", Startup.Enabled, ToggleStartup));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Designer…", null, (_, _) => OpenDesigner());
        m.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    private static ToolStripMenuItem Item(string text, bool isChecked, Action onClick) =>
        new(text, null, (_, _) => onClick()) { Checked = isChecked };

    private void ToggleStartup()
    {
        try { Startup.Enabled = !Startup.Enabled; }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _tray.ShowBalloonTip(4000, "OpenCrosshair", "Couldn't change the startup setting: " + e.Message, ToolTipIcon.Warning);
        }
    }

    private void OpenDesigner()
    {
        if (_designer is { IsDisposed: false })
        {
            _designer.Activate();
            return;
        }
        _designer = new DesignerForm(_s, Changed);
        _designer.Show();
    }

    // SystemEvents can fire on another thread; hop back to the UI thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _overlay.BeginInvoke(Redraw);

    private static Icon MakeTrayIcon()
    {
        using var mark = CrosshairRenderer.Render(new CrosshairStyle { Length = 6, Thickness = 3, Gap = 3, DotSize = 3 });
        using var canvas = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(canvas))
            g.DrawImage(mark, (32 - mark.Width) / 2, (32 - mark.Height) / 2, mark.Width, mark.Height);
        IntPtr handle = canvas.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _onTopTimer.Dispose();
        _aimTimer.Dispose();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _designer?.Close();
        _overlay.Close();
        Save();
        base.ExitThreadCore();
    }
}
