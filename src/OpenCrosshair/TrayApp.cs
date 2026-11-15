using Microsoft.Win32;
using static OpenCrosshair.Lang;

namespace OpenCrosshair;

/// <summary>The running app: tray icon, overlay, hotkeys, timers. Lives until Exit.</summary>
internal sealed class TrayApp : ApplicationContext, IDesignerHost
{
    private readonly ForegroundWatcher _foreground = new();
    private readonly RegisteredWaitHandle? _openWait;
    private readonly string _path = AppSettings.DefaultPath;
    private readonly AppSettings _s;
    private readonly OverlayWindow _overlay = new();
    private readonly NotifyIcon _tray;
    private readonly HotkeyManager _hotkeys = new();
    private readonly System.Windows.Forms.Timer _onTopTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _aimTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 33 };   // ~30 fps
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private CrosshairSprite? _sprite;   // current crosshair, rebuilt only when settings change
    private Bitmap? _frame;             // last animation frame, when the animation changes pixels
    private double _lastShot = double.NegativeInfinity;
    private DesignerForm? _designer;
    private bool _aiming;

    public TrayApp(EventWaitHandle? openSignal = null, bool openDesigner = false)
    {
        bool firstRun = !File.Exists(_path);
        _s = AppSettings.Load(_path);
        Lang.Apply(_s.Language);
        if (firstRun) _s.Active.Name = T("Default");
        Theme.Apply(_s.Theme);
        _tray = new NotifyIcon { Text = "OpenCrosshair", Icon = AppIcon.Small, Visible = true, ContextMenuStrip = new ContextMenuStrip() };
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
        _animationTimer.Tick += (_, _) => { if (_overlay.Visible) ShowFrame(); };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        RegisterHotkeys(showProblems: true);
        _foreground.Changed += OnForegroundChanged;
        // A second launch (e.g. the taskbar pin) signals this copy; the wait runs on a pool thread, so hop to the UI thread.
        if (openSignal is not null)
            _openWait = ThreadPool.RegisterWaitForSingleObject(openSignal, (_, _) => _overlay.BeginInvoke(OpenDesigner), null, -1, false);
        try
        {
            if (Startup.Enabled) Startup.Enabled = true;   // refresh the entry: current location, and start in the tray
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }

        if (firstRun)
        {
            _tray.ShowBalloonTip(10000, T("OpenCrosshair is running"),
                T("Your crosshair is in the middle of the screen. Double-click this icon (by the clock) or press {0} to customize it.", _s.Hotkeys["Designer"]),
                ToolTipIcon.Info);
            Save();   // so the tip shows only once
        }
        if (openDesigner) OpenDesigner();
    }

    /// <summary>Another window came to the front: get back on top right away, and switch to that game's crosshair if one is linked.</summary>
    private void OnForegroundChanged(IntPtr window)
    {
        _overlay.KeepOnTop();
        if (_foreground.ExeOf(window) is not { } exe) return;
        int linked = _s.ProfileForGame(exe);
        if (linked >= 0 && linked != _s.ActiveProfile) SelectProfile(linked);
    }

    private void SetTheme(AppTheme theme)
    {
        if (theme == _s.Theme) return;
        _s.Theme = theme;
        Theme.Apply(theme);
        Save();
        ReopenDesigner();
    }

    private void SetLanguage(string? code)
    {
        if (code == _s.Language) return;
        _s.Language = code;
        Lang.Apply(code);
        Save();
        ReopenDesigner();   // the tray menu is rebuilt each time it opens
    }

    private void ReopenDesigner()
    {
        // Windows pick up the theme and language when they're created, so reopen the designer. Deferred: this runs from its own event.
        if (_designer is { IsDisposed: false })
            _overlay.BeginInvoke(() =>
            {
                _designer?.Close();
                _designer = null;
                OpenDesigner();
            });
    }

    private Screen SelectedScreen() =>
        Screen.AllScreens.FirstOrDefault(x => x.DeviceName == _s.Monitor) ?? Screen.PrimaryScreen!;

    private void Redraw()
    {
        var old = _sprite;
        _sprite = new CrosshairSprite(_s.Active);
        _animationTimer.Enabled = _sprite.IsAnimated;
        ShowFrame();
        old?.Dispose();   // only after the overlay has switched to the new bitmaps
    }

    private void ShowFrame()
    {
        double seconds = _clock.Elapsed.TotalSeconds;
        // Shoot polls the button state (no mouse hook, nothing is intercepted) and only reacts while a game is in front.
        if (_s.Active.Animation == CrosshairAnimation.Shoot && (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0
            && _foreground.GameInFront(exe => _s.ProfileForGame(exe) >= 0))
            _lastShot = seconds;
        var oldFrame = _frame;
        var image = _sprite!.Frame(seconds, seconds - _lastShot, out double opacity, out _frame);
        var screen = SelectedScreen();
        var off = _s.Offsets.GetValueOrDefault(screen.DeviceName) ?? new Offset(0, 0);
        byte alpha = (byte)Math.Round(_s.Active.OpacityPercent * 2.55 * opacity);
        _overlay.SetImage(image, alpha, screen.Bounds, new Point(off.X, off.Y));
        oldFrame?.Dispose();
    }

    private void Save()
    {
        try { _s.Save(_path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _tray.ShowBalloonTip(4000, "OpenCrosshair", T("Couldn't save settings: {0}", e.Message), ToolTipIcon.Warning);
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
        bool held = (Native.GetAsyncKeyState((int)_s.AimButton) & 0x8000) != 0;
        if (held == _aiming) return;
        _aiming = held;
        ApplyVisibility();
    }

    private void SetHideWhileAiming(bool on, AimButton button)
    {
        _s.HideWhileAiming = on;
        _s.AimButton = button;
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

    /// <summary>(Re)registers every hotkey from the settings. Empty ones are switched off. Returns the ones that didn't work.</summary>
    private List<string> RegisterHotkeys(bool showProblems)
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
        var failed = actions
            .Where(a => !string.IsNullOrWhiteSpace(_s.Hotkeys[a.Key]) && !_hotkeys.Register(_s.Hotkeys[a.Key], a.Value.Run, a.Value.Repeat))
            .Select(a => $"{AppSettings.HotkeyLabel(a.Key)} ({_s.Hotkeys[a.Key]})").ToList();
        if (showProblems && failed.Count > 0)
            _tray.ShowBalloonTip(6000, "OpenCrosshair",
                T("These hotkeys are invalid or used by another app: {0}", string.Join(", ", failed)), ToolTipIcon.Warning);
        return failed;
    }

    // ---- what the customize window asks of the app ----
    void IDesignerHost.Changed() => Changed();
    void IDesignerHost.SetTheme(AppTheme theme) => SetTheme(theme);
    void IDesignerHost.SetLanguage(string? code) => SetLanguage(code);
    IReadOnlyList<string> IDesignerHost.ApplyHotkeys()
    {
        var failed = RegisterHotkeys(showProblems: false);
        Save();
        return failed;
    }
    void IDesignerHost.PauseHotkeys(bool paused)
    {
        if (paused) _hotkeys.UnregisterAll();   // so the keys can be pressed into the hotkey boxes
        else RegisterHotkeys(showProblems: false);
    }

    private void BuildMenu()
    {
        var m = _tray.ContextMenuStrip!;
        m.Items.Clear();
        var customize = Item(T("Customize…"), false, OpenDesigner);
        customize.Font = new Font(customize.Font, FontStyle.Bold);   // the double-click action, like Windows does it
        m.Items.Add(customize);
        m.Items.Add(Item(T("Show crosshair"), _s.Visible, ToggleVisible));

        var profiles = Item(T("Crosshair"), false, () => { });
        for (int i = 0; i < _s.Profiles.Count; i++)
        {
            int index = i;
            profiles.DropDownItems.Add(Item(_s.Profiles[i].Name, i == _s.ActiveProfile, () => SelectProfile(index)));
        }
        m.Items.Add(profiles);

        var displays = Item(T("Display"), false, () => { });
        var current = SelectedScreen().DeviceName;
        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var scr = screens[i];
            var label = scr.Primary
                ? T("Display {0} ({1}×{2}, primary)", i + 1, scr.Bounds.Width, scr.Bounds.Height)
                : T("Display {0} ({1}×{2})", i + 1, scr.Bounds.Width, scr.Bounds.Height);
            displays.DropDownItems.Add(Item(label, scr.DeviceName == current, () => { _s.Monitor = scr.DeviceName; Changed(); }));
        }
        m.Items.Add(displays);

        var aim = Item(T("Hide while aiming"), false, () => { });
        aim.DropDownItems.Add(Item(T("Off"), !_s.HideWhileAiming, () => SetHideWhileAiming(false, _s.AimButton)));
        foreach (var (button, label) in new[]
        {
            (AimButton.Right, T("While holding the right button")), (AimButton.Middle, T("While holding the middle button")),
            (AimButton.Back, T("While holding the back side button")), (AimButton.Forward, T("While holding the forward side button")),
        })
            aim.DropDownItems.Add(Item(label, _s.HideWhileAiming && _s.AimButton == button, () => SetHideWhileAiming(true, button)));
        m.Items.Add(aim);
        var theme = Item(T("Theme"), false, () => { });
        foreach (var (value, label) in new[] { (AppTheme.Dark, T("Dark")), (AppTheme.Light, T("Light")), (AppTheme.System, T("Match Windows")) })
            theme.DropDownItems.Add(Item(label, _s.Theme == value, () => SetTheme(value)));
        m.Items.Add(theme);
        var language = Item(T("Language"), false, () => { });
        language.DropDownItems.Add(Item(T("Match Windows"), _s.Language is null, () => SetLanguage(null)));
        foreach (var (code, name) in Lang.Available)
            language.DropDownItems.Add(Item(name, _s.Language == code, () => SetLanguage(code)));
        m.Items.Add(language);
        m.Items.Add(Item(T("Start with Windows"), Startup.Enabled, ToggleStartup));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item(T("Exit"), false, ExitThread));
        m.BackColor = Theme.MenuBack;
    }

    private static ToolStripMenuItem Item(string text, bool isChecked, Action onClick)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => onClick()) { Checked = isChecked, ForeColor = Theme.MenuText };
        item.DropDown.BackColor = Theme.MenuBack;
        return item;
    }

    private void ToggleStartup()
    {
        try { Startup.Enabled = !Startup.Enabled; }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _tray.ShowBalloonTip(4000, "OpenCrosshair", T("Couldn't change the startup setting: {0}", e.Message), ToolTipIcon.Warning);
        }
    }

    private void OpenDesigner()
    {
        if (_designer is { IsDisposed: false })
        {
            _designer.Activate();
            return;
        }
        _designer = new DesignerForm(_s, this);
        _designer.Show();
    }

    // SystemEvents can fire on another thread; hop back to the UI thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _overlay.BeginInvoke(Redraw);

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _openWait?.Unregister(null);
        _foreground.Dispose();
        _onTopTimer.Dispose();
        _aimTimer.Dispose();
        _animationTimer.Dispose();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _designer?.Close();
        _overlay.Close();
        _frame?.Dispose();
        _sprite?.Dispose();
        Save();
        base.ExitThreadCore();
    }
}
