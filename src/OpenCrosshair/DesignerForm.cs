using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenCrosshair;

/// <summary>What the customize window needs from the running app.</summary>
internal interface IDesignerHost
{
    /// <summary>A crosshair setting changed: redraw it on screen and save.</summary>
    void Changed();
    void SetTheme(AppTheme theme);
    /// <summary>Re-registers the hotkeys from the settings and saves; returns the ones that couldn't be used.</summary>
    IReadOnlyList<string> ApplyHotkeys();
    /// <summary>While a hotkey box is focused the hotkeys are paused, so their keys can be typed into it.</summary>
    void PauseHotkeys(bool paused);
}

/// <summary>
/// The "customize" window: saved crosshairs and presets on the left, a live preview in the middle,
/// settings in sections on the right (plain labels, sliders, color swatches, hover tips), share codes at the bottom.
/// </summary>
internal sealed class DesignerForm : Form
{
    internal static readonly (string Name, CrosshairStyle Style)[] Presets =
    [
        ("Classic cross", new CrosshairStyle()),
        ("Cross with dot", new CrosshairStyle { Gap = 4, DotSize = 2 }),
        ("Dot only", new CrosshairStyle { Length = 0, DotSize = 4, RoundDot = true }),
        ("T-shape", new CrosshairStyle { TStyle = true, Length = 8 }),
        ("X", new CrosshairStyle { Rotation = 45, Length = 8, Gap = 4 }),
        ("Circle and dot", new CrosshairStyle { Length = 0, CircleRadius = 14, CircleThickness = 2, DotSize = 2, RoundDot = true }),
        ("Chevron", new CrosshairStyle { Length = 0, Shape = CrosshairShape.Chevron, ShapeSize = 14, Gap = 2 }),
        ("Pulsing dot", new CrosshairStyle { Length = 0, DotSize = 5, RoundDot = true, Animation = CrosshairAnimation.Breathe }),
        ("Shooting cross", new CrosshairStyle { Gap = 4, Animation = CrosshairAnimation.Shoot }),
    ];

    private static readonly (CrosshairAnimation Value, string Label, string Help)[] Animations =
    [
        (CrosshairAnimation.None, "None", "The crosshair stays still."),
        (CrosshairAnimation.Shoot, "Shoot", "Opens up every time you left-click, then eases back. The preview fires every 1.5 s."),
        (CrosshairAnimation.Breathe, "Breathe", "Slowly fades in and out."),
        (CrosshairAnimation.Blink, "Blink", "Switches on and off."),
        (CrosshairAnimation.Rotate, "Rotate", "Spins around its center."),
        (CrosshairAnimation.Rainbow, "Rainbow", "Cycles through every color."),
    ];

    private readonly AppSettings _s;
    private readonly IDesignerHost _host;
    private readonly Label _hotkeyHint = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 0), Tag = "subtle" };
    private readonly Label _hotkeyStatus = new() { AutoSize = true, MaximumSize = new Size(320, 0), Tag = "subtle" };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 44, 52) };
    private readonly TextBox _code = new() { Dock = DockStyle.Fill, PlaceholderText = "Paste a share code here, then press Import" };
    private readonly Label _animationHelp = new() { AutoSize = true, MaximumSize = new Size(240, 0) };
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 300 };
    private readonly List<Action> _refreshers = [];   // copy the active crosshair's values into the controls
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 33 };   // animates the preview, ~30 fps
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private CrosshairSprite? _sprite;
    private bool _syncing;   // controls are being filled from settings, so their change events mustn't write back

    public DesignerForm(AppSettings settings, IDesignerHost host)
    {
        _s = settings;
        _host = host;
        // Sizes below are for 100 % display scaling; WinForms grows everything to match the actual scaling.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "OpenCrosshair – customize your crosshair";
        Icon = AppIcon.Large;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(1200, 680);
        MinimumSize = new Size(1060, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 540));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(BuildCrosshairList(), 0, 0);
        layout.Controls.Add(BuildPreview(), 1, 0);
        layout.Controls.Add(BuildSettings(), 2, 0);
        var bottom = BuildBottom();
        layout.Controls.Add(bottom, 0, 1);
        layout.SetColumnSpan(bottom, 3);
        Controls.Add(layout);

        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _list.SelectedIndex < 0 || _list.SelectedIndex == _s.ActiveProfile) return;
            _s.ActiveProfile = _list.SelectedIndex;
            ShowActive();
            _host.Changed();
        };
        _preview.Paint += DrawPreview;
        _preview.Resize += (_, _) => _preview.Invalidate();
        _previewTimer.Tick += (_, _) => _preview.Invalidate();
        FormClosed += (_, _) =>
        {
            _previewTimer.Dispose();
            _sprite?.Dispose();
        };
        Theme.Style(this);   // before SyncFromSettings, which paints the color swatches
        Theme.Style(_tips);
        RefreshHotkeyHint();
        SyncFromSettings();
    }

    // ---- layout ----------------------------------------------------------------------------------

    private Control BuildCrosshairList()
    {
        var presets = new ComboBox { Dock = DockStyle.Bottom, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 6, 0, 0), Name = "Presets" };
        presets.Items.Add("+ Add a ready-made one…");
        foreach (var (name, _) in Presets) presets.Items.Add(name);
        presets.SelectedIndex = 0;
        presets.SelectedIndexChanged += (_, _) =>
        {
            if (presets.SelectedIndex <= 0) return;
            var style = Presets[presets.SelectedIndex - 1].Style.Clone();
            style.Name = Presets[presets.SelectedIndex - 1].Name;
            presets.SelectedIndex = 0;
            AddAndSelect(style);
        };
        _tips.SetToolTip(presets, "Start from a ready-made design, then change anything you like");

        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
        for (int i = 0; i < 3; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        foreach (var b in new[] { Button("New", AddNew), Button("Copy", Duplicate), Button("Delete", Delete) })
        {
            b.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            buttons.Controls.Add(b);
        }

        var library = Link("Community library", () => OpenInBrowser(Community.LibraryUrl));
        var share = Link("Share yours in the library…", () => OpenInBrowser(Community.ShareUrl(_s.Active)));
        _tips.SetToolTip(library, "Opens crosshairs shared by other players on GitHub. Copy a code there, paste it below and press Import.");
        _tips.SetToolTip(share, "Opens a form on GitHub with this crosshair's name and share code filled in (needs a free GitHub account)");
        var links = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 6, 0, 0) };
        links.Controls.AddRange([library, share]);

        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(_list);
        panel.Controls.Add(presets);
        panel.Controls.Add(buttons);
        panel.Controls.Add(links);
        panel.Controls.Add(Heading("Your crosshairs"));
        _tips.SetToolTip(_list, "Click one to use it. Switch while playing with Ctrl+Alt+N.");
        return panel;
    }

    private Control BuildPreview()
    {
        var hint = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 40, TextAlign = ContentAlignment.MiddleCenter, Tag = "subtle",
            ForeColor = Theme.Subtle, Text = "This is how it looks on screen. Every change is saved and shown right away.",
        };
        _preview.Tag = "keep";   // stays dark in both themes, so every crosshair color is easy to see
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(_preview);
        panel.Controls.Add(hint);
        panel.Controls.Add(Heading("Preview"));
        return panel;
    }

    private Control BuildSettings()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0) };
        var sections = new (string Title, TableLayoutPanel Panel)[]
        {
            ("Basics", BasicsSection()), ("Lines", LinesSection()), ("Dot & circle", DotSection()),
            ("Shapes & text", ExtrasSection()), ("Image", ImageSection()), ("Animation", AnimationSection()),
            ("Hotkeys", HotkeysSection()),
        };
        foreach (var (title, section) in sections)
        {
            // A last row that takes the leftover height keeps the real rows packed at the top.
            section.RowCount++;
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            // Radio buttons drawn as buttons act as tabs; unlike TabControl they can be colored for the dark theme.
            var tab = new RadioButton
            {
                Text = title, Appearance = Appearance.Button, AutoSize = true, FlatStyle = FlatStyle.Flat,
                UseMnemonic = false, Margin = new Padding(0, 0, 3, 4), Padding = new Padding(1, 2, 1, 2),
            };
            tab.FlatAppearance.CheckedBackColor = Theme.Accent;
            tab.CheckedChanged += (_, _) =>
            {
                section.Visible = tab.Checked;
                tab.ForeColor = tab.Checked ? Color.White : Theme.Text;
            };
            tabs.Controls.Add(tab);
            host.Controls.Add(section);
        }
        host.Controls.Add(tabs);
        ((RadioButton)tabs.Controls[0]).Checked = true;

        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(host);
        panel.Controls.Add(Heading("Settings"));
        return panel;
    }

    private Control BuildBottom()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 9, Margin = new Padding(0, 8, 0, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(new Label { Text = "Share code:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        bar.Controls.Add(_code, 1, 0);
        bar.Controls.Add(Button("Copy mine", CopyCode), 2, 0);
        bar.Controls.Add(Button("Import", ImportCode), 3, 0);
        var saveFile = Button("Save file…", SaveFile);
        var openFile = Button("Open file…", OpenFile);
        bar.Controls.Add(saveFile, 4, 0);
        bar.Controls.Add(openFile, 5, 0);
        bar.Controls.Add(Button("Save as PNG", ExportPng), 6, 0);
        _tips.SetToolTip(_code, "Share codes contain every setting except a custom picture. To share a picture too, use Save file.");
        _tips.SetToolTip(saveFile, "Saves this crosshair, including its picture, as one .ochair file you can send to anyone");
        _tips.SetToolTip(openFile, "Adds a crosshair from an .ochair file");

        var theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Anchor = AnchorStyles.Right, Name = "Theme" };
        theme.Items.AddRange(["Dark theme", "Light theme", "Match Windows"]);
        theme.SelectedIndex = (int)_s.Theme;
        theme.SelectedIndexChanged += (_, _) => _host.SetTheme((AppTheme)theme.SelectedIndex);
        bar.Controls.Add(theme, 8, 0);

        _hotkeyHint.ForeColor = Theme.Subtle;
        bar.Controls.Add(_hotkeyHint, 0, 1);
        bar.SetColumnSpan(_hotkeyHint, 9);
        return bar;
    }

    private void RefreshHotkeyHint()
    {
        string Key(string action) => string.IsNullOrWhiteSpace(_s.Hotkeys[action]) ? "(off)" : _s.Hotkeys[action];
        _hotkeyHint.Text = $"Hotkeys:  {Key("Toggle")} show/hide   ·   {Key("NextProfile")} next crosshair   ·   "
            + $"{Key("NudgeUp").Replace("Up", "Arrows")} move   ·   {Key("ResetOffset")} back to center   ·   {Key("Designer")} open this window"
            + "   (change them in the Hotkeys tab)";
    }

    // ---- settings sections -----------------------------------------------------------------------

    private TableLayoutPanel BasicsSection()
    {
        var t = Section();
        Row(t, "Name", TextBoxFor(nameof(CrosshairStyle.Name), 40, s => s.Name, (s, v) => s.Name = v));
        Row(t, "Color", ColorPicker(nameof(CrosshairStyle.Color), s => s.Color, (s, v) => s.Color = v));
        Row(t, "Opacity", Slider(nameof(CrosshairStyle.OpacityPercent), 5, 100, "%", s => s.OpacityPercent, (s, v) => s.OpacityPercent = v));
        Row(t, "", Check(nameof(CrosshairStyle.Outline), "Dark outline around everything", s => s.Outline, (s, v) => s.Outline = v));
        Row(t, "Outline width", Slider(nameof(CrosshairStyle.OutlineThickness), 1, 5, "px", s => s.OutlineThickness, (s, v) => s.OutlineThickness = v));
        Row(t, "Outline color", ColorPicker(nameof(CrosshairStyle.OutlineColor), s => s.OutlineColor, (s, v) => s.OutlineColor = v));
        Row(t, "Rotation", Slider(nameof(CrosshairStyle.Rotation), 0, 359, "°", s => s.Rotation, (s, v) => s.Rotation = v));
        Row(t, "Soft edges", Slider(nameof(CrosshairStyle.Blur), 0, 10, "px", s => s.Blur, (s, v) => s.Blur = v));
        Row(t, "Use in games", GamesPicker(), nameof(CrosshairStyle.Games));
        return t;
    }

    /// <summary>Links this crosshair to games: it's switched to automatically when one of them comes to the front.</summary>
    private Control GamesPicker()
    {
        var games = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Name = "GamesList", Margin = new Padding(0, 6, 0, 4) };
        var link = Button("Link a running game…", () =>
        {
            using var picker = new GamePicker();
            if (picker.ShowDialog(this) == DialogResult.OK && picker.Chosen is { } exe) LinkGame(exe);
        });
        var browse = Button("Choose its .exe…", () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
            if (dialog.ShowDialog(this) == DialogResult.OK) LinkGame(Path.GetFileName(dialog.FileName));
        });
        var clear = Button("Unlink all", () =>
        {
            _s.Active.Games = null;
            Apply();
            ShowGames();
        });
        void ShowGames() => games.Text = _s.Active.Games is { Count: > 0 } list
            ? "Switches to this crosshair in: " + string.Join(", ", list)
            : "Not linked to any game.";
        void LinkGame(string exe)
        {
            exe = exe.ToLowerInvariant();
            foreach (var other in _s.Profiles) other.Games?.Remove(exe);   // a game belongs to one crosshair
            (_s.Active.Games ??= []).Add(exe);
            Apply();
            ShowGames();
        }
        _refreshers.Add(ShowGames);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        buttons.Controls.AddRange([link, browse, clear]);
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        panel.Controls.AddRange([games, buttons]);
        return panel;
    }

    private TableLayoutPanel HotkeysSection()
    {
        var t = Section();
        foreach (var action in AppSettings.DefaultHotkeys.Keys) Row(t, AppSettings.HotkeyLabel(action), HotkeyBox(action));
        var reset = Button("Back to the defaults", () =>
        {
            foreach (var (action, combo) in AppSettings.DefaultHotkeys) _s.Hotkeys[action] = combo;
            ApplyHotkeys();
            ShowActive();
        });
        reset.Anchor = AnchorStyles.Left;
        Row(t, "", reset);
        _hotkeyStatus.ForeColor = Theme.Subtle;
        _hotkeyStatus.Text = "Click a box, then press the keys you want. Backspace switches a hotkey off.";
        Row(t, "", _hotkeyStatus);
        return t;
    }

    /// <summary>A box that records the key combination you press (e.g. Ctrl+Alt+H).</summary>
    private Control HotkeyBox(string action)
    {
        var box = new TextBox { ReadOnly = true, Name = "Hotkey" + action, ShortcutsEnabled = false, Cursor = Cursors.Hand };
        box.GotFocus += (_, _) => _host.PauseHotkeys(true);
        box.LostFocus += (_, _) => _host.PauseHotkeys(false);
        box.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;   // nothing gets typed; also stops the "ding"
            if (e.KeyCode is Keys.Back or Keys.Delete)
            {
                _s.Hotkeys[action] = "";
                box.Text = "";
                ApplyHotkeys();
                return;
            }
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.Tab) return;
            if (!e.Control && !e.Alt && !e.Shift)
            {
                _hotkeyStatus.Text = "Add Ctrl, Alt or Shift, so the key still works normally in games.";
                return;
            }
            var combo = string.Join("+", new[] { e.Control ? "Ctrl" : null, e.Alt ? "Alt" : null, e.Shift ? "Shift" : null, e.KeyCode.ToString() }.OfType<string>());
            _s.Hotkeys[action] = combo;
            box.Text = combo;
            ApplyHotkeys();
        };
        _refreshers.Add(() => box.Text = _s.Hotkeys[action]);
        return box;
    }

    private void ApplyHotkeys()
    {
        var failed = _host.ApplyHotkeys();   // registers them, which is how a clash with another app shows up
        if (ActiveControl is TextBox { Name: var name } && name.StartsWith("Hotkey")) _host.PauseHotkeys(true);   // still typing into a box
        _hotkeyStatus.Text = failed.Count == 0
            ? "Saved. Your hotkeys are active."
            : "Saved, but these are used by another app, so they don't work: " + string.Join(", ", failed);
        RefreshHotkeyHint();
    }

    private TableLayoutPanel LinesSection()
    {
        var t = Section();
        Row(t, "Length", Slider(nameof(CrosshairStyle.Length), 0, 100, "px", s => s.Length, (s, v) => s.Length = v));
        Row(t, "Thickness", Slider(nameof(CrosshairStyle.Thickness), 1, 30, "px", s => s.Thickness, (s, v) => s.Thickness = v));
        Row(t, "Gap", Slider(nameof(CrosshairStyle.Gap), 0, 100, "px", s => s.Gap, (s, v) => s.Gap = v));
        Row(t, "Number of lines", Slider(nameof(CrosshairStyle.Arms), 2, 8, "", s => s.Arms, (s, v) => s.Arms = v));
        Row(t, "", Check(nameof(CrosshairStyle.TStyle), "Hide the top line (T-shape)", s => s.TStyle, (s, v) => s.TStyle = v));
        return t;
    }

    private TableLayoutPanel DotSection()
    {
        var t = Section();
        Row(t, "Dot size", Slider(nameof(CrosshairStyle.DotSize), 0, 30, "px", s => s.DotSize, (s, v) => s.DotSize = v));
        Row(t, "", Check(nameof(CrosshairStyle.RoundDot), "Round dot", s => s.RoundDot, (s, v) => s.RoundDot = v));
        Row(t, "Circle size", Slider(nameof(CrosshairStyle.CircleRadius), 0, 200, "px", s => s.CircleRadius, (s, v) => s.CircleRadius = v));
        Row(t, "Circle thickness", Slider(nameof(CrosshairStyle.CircleThickness), 1, 20, "px", s => s.CircleThickness, (s, v) => s.CircleThickness = v));
        Row(t, "Circle amount", Slider(nameof(CrosshairStyle.CircleArc), 30, 360, "°", s => s.CircleArc, (s, v) => s.CircleArc = v));
        return t;
    }

    private TableLayoutPanel ExtrasSection()
    {
        var t = Section();
        Row(t, "Shape", Choice<CrosshairShape>(nameof(CrosshairStyle.Shape),
            [(CrosshairShape.None, "None"), (CrosshairShape.Chevron, "Chevron ^"), (CrosshairShape.Triangle, "Triangle"), (CrosshairShape.Square, "Square")],
            s => s.Shape, (s, v) => s.Shape = v));
        Row(t, "Shape size", Slider(nameof(CrosshairStyle.ShapeSize), 4, 100, "px", s => s.ShapeSize, (s, v) => s.ShapeSize = v));
        Row(t, "Text", TextBoxFor(nameof(CrosshairStyle.LabelText), 30, s => s.LabelText ?? "", (s, v) => s.LabelText = v));
        Row(t, "Text size", Slider(nameof(CrosshairStyle.LabelSize), 6, 48, "px", s => s.LabelSize, (s, v) => s.LabelSize = v));
        return t;
    }

    private TableLayoutPanel ImageSection()
    {
        var t = Section();
        var file = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "No image: using the drawn crosshair" };
        var choose = Button("Choose…", () =>
        {
            using var dialog = new OpenFileDialog { Filter = "Images (PNG, GIF, BMP, JPG)|*.png;*.gif;*.bmp;*.jpg;*.jpeg|All files|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _s.Active.ImagePath = dialog.FileName;
            file.Text = dialog.FileName;
            Apply();
        });
        var remove = Button("Remove", () =>
        {
            _s.Active.ImagePath = null;
            file.Text = "";
            Apply();
        });
        var row = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(file, 0, 0);
        row.Controls.Add(choose, 1, 0);
        row.Controls.Add(remove, 2, 0);
        _refreshers.Add(() => file.Text = _s.Active.ImagePath ?? "");
        Row(t, "Picture", row, nameof(CrosshairStyle.ImagePath));
        Row(t, "Size", Slider(nameof(CrosshairStyle.ImageScalePercent), 10, 400, "%", s => s.ImageScalePercent, (s, v) => s.ImageScalePercent = v));
        Row(t, "", new Label
        {
            AutoSize = true, MaximumSize = new Size(260, 0), ForeColor = Theme.Subtle, Tag = "subtle",
            Text = "Use a PNG with a transparent background, or an animated GIF. Opacity, rotation, soft edges and animations still apply.",
        });
        return t;
    }

    private TableLayoutPanel AnimationSection()
    {
        var t = Section();
        Row(t, "Animation", Choice<CrosshairAnimation>(nameof(CrosshairStyle.Animation),
            Animations.Select(a => (a.Value, a.Label)).ToArray(), s => s.Animation, (s, v) => s.Animation = v));
        Row(t, "Speed", Slider(nameof(CrosshairStyle.AnimationSpeed), 1, 10, "", s => s.AnimationSpeed, (s, v) => s.AnimationSpeed = v));
        _animationHelp.ForeColor = Theme.Subtle;
        _animationHelp.Tag = "subtle";
        Row(t, "", _animationHelp);
        return t;
    }

    // ---- building blocks -------------------------------------------------------------------------

    private static TableLayoutPanel Section()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Visible = false, Padding = new Padding(2, 8, 12, 0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private void Row(TableLayoutPanel section, string label, Control control, string? property = null)
    {
        int row = section.RowCount++;
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        if (label.Length > 0) control.AccessibleName = label;
        property ??= control.Name.Length > 0 ? control.Name : null;
        var help = property is null ? null : TypeDescriptor.GetProperties(typeof(CrosshairStyle))[property]?.Description;
        if (!string.IsNullOrEmpty(help))
        {
            _tips.SetToolTip(caption, help);
            _tips.SetToolTip(control, help);
            foreach (Control child in control.Controls) _tips.SetToolTip(child, help);
        }
        section.Controls.Add(caption, 0, row);
        section.Controls.Add(control, 1, row);
    }

    private Control Slider(string property, int min, int max, string unit, Func<CrosshairStyle, int> get, Action<CrosshairStyle, int> set)
    {
        var bar = new TrackBar
        {
            Minimum = min, Maximum = max, TickStyle = TickStyle.None, AutoSize = false, Height = 30, Dock = DockStyle.Fill,
            Name = property + "Slider", LargeChange = Math.Max(1, (max - min) / 10), AccessibleName = property,
        };
        var number = new NumberBox { Minimum = min, Maximum = max, Width = 50, Dock = DockStyle.Right, Name = property };
        var units = new Label { Text = unit, Width = 26, Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
        var panel = new Panel { Height = 32, Name = property, Margin = new Padding(0, 2, 0, 2) };
        panel.Controls.Add(bar);
        panel.Controls.Add(number);
        panel.Controls.Add(units);
        bar.ValueChanged += (_, _) => number.Value = bar.Value;
        number.ValueChanged += (_, _) =>
        {
            bar.Value = number.Value;
            if (_syncing) return;
            set(_s.Active, number.Value);
            Apply();
        };
        _refreshers.Add(() => number.Value = Math.Clamp(get(_s.Active), min, max));
        return panel;
    }

    private Control Check(string property, string text, Func<CrosshairStyle, bool> get, Action<CrosshairStyle, bool> set)
    {
        var box = new CheckBox { Text = text, AutoSize = true, Name = property };
        box.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            set(_s.Active, box.Checked);
            Apply();
        };
        _refreshers.Add(() => box.Checked = get(_s.Active));
        return box;
    }

    private Control ColorPicker(string property, Func<CrosshairStyle, Color> get, Action<CrosshairStyle, Color> set)
    {
        var swatch = new Button { FlatStyle = FlatStyle.Flat, Height = 30, Name = property, Tag = "keep", Text = "Click to change", TextAlign = ContentAlignment.MiddleCenter };
        void Show(Color c)
        {
            swatch.BackColor = c;
            swatch.ForeColor = c.GetBrightness() > 0.55 ? Color.Black : Color.White;   // readable on any swatch
            swatch.Text = $"#{CrosshairStyle.Hex(c)}   (click to change)";
        }
        swatch.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = get(_s.Active), FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            set(_s.Active, dialog.Color);
            Show(dialog.Color);
            Apply();
        };
        _refreshers.Add(() => Show(get(_s.Active)));
        return swatch;
    }

    private Control Choice<T>(string property, (T Value, string Label)[] options, Func<CrosshairStyle, T> get, Action<CrosshairStyle, T> set)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Name = property };
        foreach (var (_, label) in options) box.Items.Add(label);
        box.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || box.SelectedIndex < 0) return;
            set(_s.Active, options[box.SelectedIndex].Value);
            Apply();
        };
        _refreshers.Add(() => box.SelectedIndex = Math.Max(0, Array.FindIndex(options, o => EqualityComparer<T>.Default.Equals(o.Value, get(_s.Active)))));
        return box;
    }

    private Control TextBoxFor(string property, int maxLength, Func<CrosshairStyle, string> get, Action<CrosshairStyle, string> set)
    {
        var box = new TextBox { MaxLength = maxLength, Name = property };
        box.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            set(_s.Active, box.Text);
            Apply();
        };
        _refreshers.Add(() => box.Text = get(_s.Active));
        return box;
    }

    private static Label Heading(string text) =>
        new() { Text = text, Dock = DockStyle.Top, AutoSize = false, Height = 30, Font = new Font("Segoe UI Semibold", 11f) };

    private static Button Button(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static LinkLabel Link(string text, Action onClick)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        link.LinkClicked += (_, _) => onClick();
        return link;
    }

    private void OpenInBrowser(string url)
    {
        try { Community.Open(url); }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Your web browser couldn't be opened. The address is:\n\n" + url, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ---- behavior --------------------------------------------------------------------------------

    /// <summary>Reloads the list after the active crosshair changed elsewhere (tray menu, hotkey).</summary>
    public void SyncFromSettings()
    {
        _syncing = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in _s.Profiles) _list.Items.Add(p.Name);
        _list.EndUpdate();
        _list.SelectedIndex = _s.ActiveProfile;
        _syncing = false;
        ShowActive();
    }

    private void ShowActive()
    {
        _syncing = true;
        foreach (var refresh in _refreshers) refresh();
        _syncing = false;
        RebuildPreview();
    }

    /// <summary>A setting was changed by the user: keep values valid, update the list and preview, save and show it on screen.</summary>
    private void Apply()
    {
        _s.Active.Clamp();
        _syncing = true;
        _list.Items[_s.ActiveProfile] = _s.Active.Name;
        _syncing = false;
        RebuildPreview();
        _host.Changed();
    }

    private void RebuildPreview()
    {
        // Built once per change, not per paint: loading an image or GIF 30 times a second would be wasteful.
        _sprite?.Dispose();
        _sprite = new CrosshairSprite(_s.Active);
        _previewTimer.Enabled = _sprite.IsAnimated;
        _animationHelp.Text = Animations.First(a => a.Value == _s.Active.Animation).Help;
        _preview.Invalidate();
    }

    private void AddAndSelect(CrosshairStyle style)
    {
        _s.Profiles.Add(style);
        _s.ActiveProfile = _s.Profiles.Count - 1;
        SyncFromSettings();
        _host.Changed();
    }

    private void AddNew() => AddAndSelect(new CrosshairStyle { Name = $"Crosshair {_s.Profiles.Count + 1}" });

    private void Duplicate()
    {
        var copy = _s.Active.Clone();
        copy.Name += " copy";
        copy.Clamp();
        AddAndSelect(copy);
    }

    private void Delete()
    {
        if (_s.Profiles.Count == 1)
        {
            MessageBox.Show(this, "Keep at least one crosshair.", Text);
            return;
        }
        if (MessageBox.Show(this, $"Delete \"{_s.Active.Name}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _s.Profiles.RemoveAt(_s.ActiveProfile);
        _s.ActiveProfile = Math.Min(_s.ActiveProfile, _s.Profiles.Count - 1);
        SyncFromSettings();
        _host.Changed();
    }

    private void CopyCode()
    {
        _code.Text = _s.Active.ToShareCode();
        Clipboard.SetText(_code.Text);
    }

    private void ImportCode()
    {
        var text = string.IsNullOrWhiteSpace(_code.Text) && Clipboard.ContainsText() ? Clipboard.GetText() : _code.Text;
        if (CrosshairStyle.TryParseShareCode(text, out var style))
        {
            AddAndSelect(style);
            _code.Clear();
        }
        else
        {
            MessageBox.Show(this, "That isn't a valid OpenCrosshair share code (it should start with OC2: or OC1:).", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveFile()
    {
        using var dialog = new SaveFileDialog { Filter = CrosshairFile.Filter, FileName = _s.Active.Name + CrosshairFile.Extension };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { CrosshairFile.Save(dialog.FileName, _s.Active); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Couldn't save the file: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog { Filter = CrosshairFile.Filter };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!CrosshairFile.TryOpen(dialog.FileName, CrosshairFile.DefaultImagesFolder, out var style, out var error, out var warning))
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        AddAndSelect(style);
        if (warning is not null) MessageBox.Show(this, warning, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportPng()
    {
        using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = _s.Active.Name + ".png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var bmp = CrosshairRenderer.Render(_s.Active);
        bmp.Save(dialog.FileName, ImageFormat.Png);
    }

    private void DrawPreview(object? sender, PaintEventArgs e)
    {
        if (_sprite is null) return;
        double seconds = _clock.Elapsed.TotalSeconds;
        // The preview can't see your clicks, so Shoot fires a pretend shot every 1.5 s.
        var bmp = _sprite.Frame(seconds, seconds % 1.5, out double opacity, out var owned);
        using (owned)
        {
            // Zoom from the still size, so it doesn't jump while frames grow and shrink.
            int zoom = Math.Clamp(Math.Min(_preview.Width, _preview.Height) / Math.Max(1, _sprite.StillSize.Width), 1, 8);
            int size = bmp.Width * zoom;
            var dest = new Rectangle((_preview.Width - size) / 2, (_preview.Height - size) / 2, size, size);
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)(_s.Active.OpacityPercent / 100.0 * opacity) });
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attributes);
            var note = $"{zoom}× zoom";
            if (_sprite.ImageMissing) note += "   ·   picture not found or unreadable, showing the drawn crosshair";
            TextRenderer.DrawText(e.Graphics, note, Font, new Point(8, 8), Color.Gainsboro);
        }
    }
}
