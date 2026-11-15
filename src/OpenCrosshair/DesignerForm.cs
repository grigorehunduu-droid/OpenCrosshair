using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static OpenCrosshair.Lang;

namespace OpenCrosshair;

/// <summary>What the customize window needs from the running app.</summary>
internal interface IDesignerHost
{
    /// <summary>A crosshair setting changed: redraw it on screen and save.</summary>
    void Changed();
    void SetTheme(AppTheme theme);
    /// <summary>A code from Lang.Available, or null for Windows' language.</summary>
    void SetLanguage(string? code);
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
    // Properties, not fields, so the names are in the language chosen when they're read.
    internal static (string Name, CrosshairStyle Style)[] Presets =>
    [
        (T("Classic cross"), new CrosshairStyle()),
        (T("Cross with dot"), new CrosshairStyle { Gap = 4, DotSize = 2 }),
        (T("Dot only"), new CrosshairStyle { Length = 0, DotSize = 4, RoundDot = true }),
        (T("T-shape"), new CrosshairStyle { TStyle = true, Length = 8 }),
        ("X", new CrosshairStyle { Rotation = 45, Length = 8, Gap = 4 }),
        (T("Circle and dot"), new CrosshairStyle { Length = 0, CircleRadius = 14, CircleThickness = 2, DotSize = 2, RoundDot = true }),
        (T("Chevron"), new CrosshairStyle { Length = 0, Shape = CrosshairShape.Chevron, ShapeSize = 14, Gap = 2 }),
        (T("Pulsing dot"), new CrosshairStyle { Length = 0, DotSize = 5, RoundDot = true, Animation = CrosshairAnimation.Breathe }),
        (T("Shooting cross"), new CrosshairStyle { Gap = 4, Animation = CrosshairAnimation.Shoot }),
    ];

    private static (CrosshairAnimation Value, string Label, string Help)[] Animations =>
    [
        (CrosshairAnimation.None, T("None"), T("The crosshair stays still.")),
        (CrosshairAnimation.Shoot, T("Shoot"), T("Opens up every time you left-click, then eases back. The preview fires every 1.5 s.")),
        (CrosshairAnimation.Breathe, T("Breathe"), T("Slowly fades in and out.")),
        (CrosshairAnimation.Blink, T("Blink"), T("Switches on and off.")),
        (CrosshairAnimation.Rotate, T("Rotate"), T("Spins around its center.")),
        (CrosshairAnimation.Rainbow, T("Rainbow"), T("Cycles through every color.")),
    ];

    private readonly AppSettings _s;
    private readonly IDesignerHost _host;
    private readonly Label _hotkeyHint = new() { AutoSize = true, Margin = new Padding(0, 8, 0, 0), Tag = "subtle" };
    private readonly Label _hotkeyStatus = new() { AutoSize = true, MaximumSize = new Size(320, 0), Tag = "subtle" };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 44, 52) };
    private readonly TextBox _code = new() { Dock = DockStyle.Fill, PlaceholderText = T("Paste a share code here, then press Import") };
    private readonly Label _animationHelp = new() { AutoSize = true, MaximumSize = new Size(240, 0) };
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 300 };
    private readonly List<Action> _refreshers = [];   // copy the active crosshair's values into the controls
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 33 };   // animates the preview, ~30 fps
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private CrosshairSprite? _sprite;
    private Tour? _tour;
    private bool _syncing;   // controls are being filled from settings, so their change events mustn't write back

    public DesignerForm(AppSettings settings, IDesignerHost host)
    {
        _s = settings;
        _host = host;
        // Sizes below are for 100 % display scaling; WinForms grows everything to match the actual scaling.
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "OpenCrosshair – " + T("customize your crosshair");
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
        Shown += (_, _) =>
        {
            if (!_s.TourSeen) StartTour();
        };
        FormClosed += (_, _) =>
        {
            _tour?.Dispose();
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
        var presetList = Presets;
        presets.Items.Add(T("+ Add a ready-made one…"));
        foreach (var (name, _) in presetList) presets.Items.Add(name);
        presets.SelectedIndex = 0;
        presets.SelectedIndexChanged += (_, _) =>
        {
            if (presets.SelectedIndex <= 0) return;
            var style = presetList[presets.SelectedIndex - 1].Style.Clone();
            style.Name = presetList[presets.SelectedIndex - 1].Name;
            presets.SelectedIndex = 0;
            AddAndSelect(style);
        };
        _tips.SetToolTip(presets, T("Start from a ready-made design, then change anything you like"));

        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
        for (int i = 0; i < 3; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        foreach (var b in new[] { Button(T("New"), AddNew), Button(T("Copy"), Duplicate), Button(T("Delete"), Delete) })
        {
            b.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            buttons.Controls.Add(b);
        }

        var library = Link(T("Community library"), () => OpenInBrowser(Community.LibraryUrl));
        library.Name = "Library";
        var share = Link(T("Share yours in the library…"), () => OpenInBrowser(Community.ShareUrl(_s.Active)));
        var tour = Link(T("Take the tour"), StartTour);
        _tips.SetToolTip(library, T("Opens crosshairs shared by other players on GitHub. Copy a code there, paste it below and press Import."));
        _tips.SetToolTip(share, T("Opens a form on GitHub with this crosshair's name and share code filled in (needs a free GitHub account)"));
        _tips.SetToolTip(tour, T("A one-minute guided tour of this window"));
        var links = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(0, 6, 0, 0) };
        links.Controls.AddRange([library, share, tour]);

        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(_list);
        panel.Controls.Add(presets);
        panel.Controls.Add(buttons);
        panel.Controls.Add(links);
        panel.Controls.Add(Heading(T("Your crosshairs")));
        _tips.SetToolTip(_list, T("Click one to use it. Switch while playing with {0}.", _s.Hotkeys["NextProfile"]));
        return panel;
    }

    private Control BuildPreview()
    {
        var hint = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = 40, TextAlign = ContentAlignment.MiddleCenter, Tag = "subtle",
            ForeColor = Theme.Subtle, Text = T("This is how it looks on screen. Every change is saved and shown right away."),
        };
        _preview.Tag = "keep";   // stays dark in both themes, so every crosshair color is easy to see
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(_preview);
        panel.Controls.Add(hint);
        panel.Controls.Add(Heading(T("Preview")));
        return panel;
    }

    private Control BuildSettings()
    {
        var host = new Panel { Dock = DockStyle.Fill };
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0), Name = "Tabs" };
        var sections = new (string Title, TableLayoutPanel Panel)[]
        {
            (T("Basics"), BasicsSection()), (T("Lines"), LinesSection()), (T("Dot & circle"), DotSection()),
            (T("Shapes & text"), ExtrasSection()), (T("Image"), ImageSection()), (T("Animation"), AnimationSection()),
            (T("Hotkeys"), HotkeysSection()),
        };
        foreach (var (title, section) in sections)
        {
            // A last row that takes the leftover height keeps the real rows packed at the top.
            section.RowCount++;
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            // Radio buttons drawn as buttons act as tabs; unlike TabControl they can be colored for the dark theme.
            var tab = new RadioButton
            {
                Text = title, Name = "Tab" + tabs.Controls.Count, Appearance = Appearance.Button, AutoSize = true, FlatStyle = FlatStyle.Flat,
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
        panel.Controls.Add(Heading(T("Settings")));
        return panel;
    }

    private Control BuildBottom()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 9, Margin = new Padding(0, 8, 0, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(new Label { Text = T("Share code:"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        bar.Controls.Add(_code, 1, 0);
        bar.Controls.Add(Button(T("Copy mine"), CopyCode), 2, 0);
        bar.Controls.Add(Button(T("Import"), ImportCode), 3, 0);
        var saveFile = Button(T("Save file…"), SaveFile);
        var openFile = Button(T("Open file…"), OpenFile);
        bar.Controls.Add(saveFile, 4, 0);
        bar.Controls.Add(openFile, 5, 0);
        bar.Controls.Add(Button(T("Save as PNG"), ExportPng), 6, 0);
        _tips.SetToolTip(_code, T("Share codes contain every setting except a custom picture. To share a picture too, use Save file."));
        _tips.SetToolTip(saveFile, T("Saves this crosshair, including its picture, as one .ochair file you can send to anyone"));
        _tips.SetToolTip(openFile, T("Adds a crosshair from an .ochair file"));

        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Anchor = AnchorStyles.Right, Name = "Language" };
        language.Items.Add(T("Windows language"));
        foreach (var (_, name) in Lang.Available) language.Items.Add(name);
        language.SelectedIndex = Array.FindIndex(Lang.Available, l => l.Code == _s.Language) + 1;   // -1 + 1 = Windows language
        language.SelectedIndexChanged += (_, _) => _host.SetLanguage(language.SelectedIndex == 0 ? null : Lang.Available[language.SelectedIndex - 1].Code);
        _tips.SetToolTip(language, T("The app's language. Spotted a mistake in a translation? Fixes are welcome on GitHub."));
        bar.Controls.Add(language, 7, 0);

        var theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Anchor = AnchorStyles.Right, Name = "Theme" };
        theme.Items.AddRange([T("Dark theme"), T("Light theme"), T("Match Windows")]);
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
        string Key(string action) => string.IsNullOrWhiteSpace(_s.Hotkeys[action]) ? T("(off)") : _s.Hotkeys[action];
        _hotkeyHint.Text = T("Hotkeys:  {0} show/hide   ·   {1} next crosshair   ·   {2} move   ·   {3} back to center   ·   {4} open this window   (change them in the Hotkeys tab)",
            Key("Toggle"), Key("NextProfile"), Key("NudgeUp").Replace("Up", T("Arrows")), Key("ResetOffset"), Key("Designer"));
    }

    // ---- settings sections -----------------------------------------------------------------------

    private TableLayoutPanel BasicsSection()
    {
        var t = Section();
        Row(t, T("Name"), TextBoxFor(nameof(CrosshairStyle.Name), 40, s => s.Name, (s, v) => s.Name = v));
        Row(t, T("Color"), ColorPicker(nameof(CrosshairStyle.Color), s => s.Color, (s, v) => s.Color = v));
        Row(t, T("Opacity"), Slider(nameof(CrosshairStyle.OpacityPercent), 5, 100, "%", s => s.OpacityPercent, (s, v) => s.OpacityPercent = v));
        Row(t, "", Check(nameof(CrosshairStyle.Outline), T("Dark outline around everything"), s => s.Outline, (s, v) => s.Outline = v));
        Row(t, T("Outline width"), Slider(nameof(CrosshairStyle.OutlineThickness), 1, 5, "px", s => s.OutlineThickness, (s, v) => s.OutlineThickness = v));
        Row(t, T("Outline color"), ColorPicker(nameof(CrosshairStyle.OutlineColor), s => s.OutlineColor, (s, v) => s.OutlineColor = v));
        Row(t, T("Rotation"), Slider(nameof(CrosshairStyle.Rotation), 0, 359, "°", s => s.Rotation, (s, v) => s.Rotation = v));
        Row(t, T("Soft edges"), Slider(nameof(CrosshairStyle.Blur), 0, 10, "px", s => s.Blur, (s, v) => s.Blur = v));
        Row(t, T("Use in games"), GamesPicker(), nameof(CrosshairStyle.Games));
        return t;
    }

    /// <summary>Links this crosshair to games: it's switched to automatically when one of them comes to the front.</summary>
    private Control GamesPicker()
    {
        var games = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Name = "GamesList", Margin = new Padding(0, 6, 0, 4) };
        var link = Button(T("Link a running game…"), () =>
        {
            using var picker = new GamePicker();
            if (picker.ShowDialog(this) == DialogResult.OK && picker.Chosen is { } exe) LinkGame(exe);
        });
        var browse = Button(T("Choose its .exe…"), () =>
        {
            using var dialog = new OpenFileDialog { Filter = T("Programs") + " (*.exe)|*.exe" };
            if (dialog.ShowDialog(this) == DialogResult.OK) LinkGame(Path.GetFileName(dialog.FileName));
        });
        var clear = Button(T("Unlink all"), () =>
        {
            _s.Active.Games = null;
            Apply();
            ShowGames();
        });
        void ShowGames() => games.Text = _s.Active.Games is { Count: > 0 } list
            ? T("Switches to this crosshair in: {0}", string.Join(", ", list))
            : T("Not linked to any game.");
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
        var reset = Button(T("Back to the defaults"), () =>
        {
            foreach (var (action, combo) in AppSettings.DefaultHotkeys) _s.Hotkeys[action] = combo;
            ApplyHotkeys();
            ShowActive();
        });
        reset.Anchor = AnchorStyles.Left;
        Row(t, "", reset);
        _hotkeyStatus.ForeColor = Theme.Subtle;
        _hotkeyStatus.Text = T("Click a box, then press the keys you want. Backspace switches a hotkey off.");
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
                _hotkeyStatus.Text = T("Add Ctrl, Alt or Shift, so the key still works normally in games.");
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
            ? T("Saved. Your hotkeys are active.")
            : T("Saved, but these are used by another app, so they don't work: {0}", string.Join(", ", failed));
        RefreshHotkeyHint();
    }

    private TableLayoutPanel LinesSection()
    {
        var t = Section();
        Row(t, T("Length"), Slider(nameof(CrosshairStyle.Length), 0, 100, "px", s => s.Length, (s, v) => s.Length = v));
        Row(t, T("Thickness"), Slider(nameof(CrosshairStyle.Thickness), 1, 30, "px", s => s.Thickness, (s, v) => s.Thickness = v));
        Row(t, T("Gap"), Slider(nameof(CrosshairStyle.Gap), 0, 100, "px", s => s.Gap, (s, v) => s.Gap = v));
        Row(t, T("Number of lines"), Slider(nameof(CrosshairStyle.Arms), 2, 8, "", s => s.Arms, (s, v) => s.Arms = v));
        Row(t, "", Check(nameof(CrosshairStyle.TStyle), T("Hide the top line (T-shape)"), s => s.TStyle, (s, v) => s.TStyle = v));
        return t;
    }

    private TableLayoutPanel DotSection()
    {
        var t = Section();
        Row(t, T("Dot size"), Slider(nameof(CrosshairStyle.DotSize), 0, 30, "px", s => s.DotSize, (s, v) => s.DotSize = v));
        Row(t, "", Check(nameof(CrosshairStyle.RoundDot), T("Round dot"), s => s.RoundDot, (s, v) => s.RoundDot = v));
        Row(t, T("Circle size"), Slider(nameof(CrosshairStyle.CircleRadius), 0, 200, "px", s => s.CircleRadius, (s, v) => s.CircleRadius = v));
        Row(t, T("Circle thickness"), Slider(nameof(CrosshairStyle.CircleThickness), 1, 20, "px", s => s.CircleThickness, (s, v) => s.CircleThickness = v));
        Row(t, T("Circle amount"), Slider(nameof(CrosshairStyle.CircleArc), 30, 360, "°", s => s.CircleArc, (s, v) => s.CircleArc = v));
        return t;
    }

    private TableLayoutPanel ExtrasSection()
    {
        var t = Section();
        Row(t, T("Shape"), Choice<CrosshairShape>(nameof(CrosshairStyle.Shape),
            [(CrosshairShape.None, T("None")), (CrosshairShape.Chevron, T("Chevron") + " ^"), (CrosshairShape.Triangle, T("Triangle")), (CrosshairShape.Square, T("Square"))],
            s => s.Shape, (s, v) => s.Shape = v));
        Row(t, T("Shape size"), Slider(nameof(CrosshairStyle.ShapeSize), 4, 100, "px", s => s.ShapeSize, (s, v) => s.ShapeSize = v));
        Row(t, T("Text"), TextBoxFor(nameof(CrosshairStyle.LabelText), 30, s => s.LabelText ?? "", (s, v) => s.LabelText = v));
        Row(t, T("Text size"), Slider(nameof(CrosshairStyle.LabelSize), 6, 48, "px", s => s.LabelSize, (s, v) => s.LabelSize = v));
        return t;
    }

    private TableLayoutPanel ImageSection()
    {
        var t = Section();
        var file = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = T("No image: using the drawn crosshair") };
        var choose = Button(T("Choose…"), () =>
        {
            using var dialog = new OpenFileDialog { Filter = T("Images") + " (PNG, GIF, BMP, JPG)|*.png;*.gif;*.bmp;*.jpg;*.jpeg|" + T("All files") + "|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _s.Active.ImagePath = dialog.FileName;
            file.Text = dialog.FileName;
            Apply();
        });
        var remove = Button(T("Remove"), () =>
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
        Row(t, T("Picture"), row, nameof(CrosshairStyle.ImagePath));
        Row(t, T("Size"), Slider(nameof(CrosshairStyle.ImageScalePercent), 10, 400, "%", s => s.ImageScalePercent, (s, v) => s.ImageScalePercent = v));
        Row(t, "", new Label
        {
            AutoSize = true, MaximumSize = new Size(260, 0), ForeColor = Theme.Subtle, Tag = "subtle",
            Text = T("Use a PNG with a transparent background, or an animated GIF. Opacity, rotation, soft edges and animations still apply."),
        });
        return t;
    }

    private TableLayoutPanel AnimationSection()
    {
        var t = Section();
        Row(t, T("Animation"), Choice<CrosshairAnimation>(nameof(CrosshairStyle.Animation),
            Animations.Select(a => (a.Value, a.Label)).ToArray(), s => s.Animation, (s, v) => s.Animation = v));
        Row(t, T("Speed"), Slider(nameof(CrosshairStyle.AnimationSpeed), 1, 10, "", s => s.AnimationSpeed, (s, v) => s.AnimationSpeed = v));
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
        // Wraps instead of running under the control when a translation is long.
        var caption = new Label { Text = label, AutoSize = true, MaximumSize = new Size(124, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 6) };
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        if (label.Length > 0) control.AccessibleName = label;
        property ??= control.Name.Length > 0 ? control.Name : null;
        var help = property is null ? null : TypeDescriptor.GetProperties(typeof(CrosshairStyle))[property]?.Description;
        if (!string.IsNullOrEmpty(help))
        {
            help = T(help);
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
        var swatch = new Button { FlatStyle = FlatStyle.Flat, Height = 30, Name = property, Tag = "keep", TextAlign = ContentAlignment.MiddleCenter };
        void Show(Color c)
        {
            swatch.BackColor = c;
            swatch.ForeColor = c.GetBrightness() > 0.55 ? Color.Black : Color.White;   // readable on any swatch
            swatch.Text = $"#{CrosshairStyle.Hex(c)}   " + T("(click to change)");
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
            MessageBox.Show(this, T("Your web browser couldn't be opened. The address is:") + "\n\n" + url, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ---- tour ------------------------------------------------------------------------------------

    internal Tour? CurrentTour => _tour;

    /// <summary>Starts the guided tour of this window. It also starts by itself the first time the window opens.</summary>
    public void StartTour()
    {
        _tour?.Dispose();
        if (!_s.TourSeen)
        {
            _s.TourSeen = true;   // starts by itself once, whether it's finished or skipped
            _host.Changed();
        }
        _tour = new Tour(this, TourSteps());
        _tour.Start();
    }

    private List<TourStep> TourSteps()
    {
        Control? Named(string name) => Controls.Find(name, true).FirstOrDefault();
        RadioButton Tab(int i) => (RadioButton)Named("Tab" + i)!;
        // Where a control is on screen, with its caption when it's a settings row; null while it isn't shown.
        Func<Rectangle?> Spot(Func<Control?> find) => () =>
        {
            if (find() is not { Visible: true } c) return null;
            var area = c.RectangleToScreen(c.ClientRectangle);
            if (c.Parent is TableLayoutPanel t && t.GetColumn(c) == 1 && t.GetControlFromPosition(0, t.GetRow(c)) is Label { Text.Length: > 0 } caption)
                area = Rectangle.Union(area, caption.RectangleToScreen(caption.ClientRectangle));
            return area;
        };
        int profiles = 0, length = 0;   // what a "Try it" step started from
        return
        [
            new(T("Welcome to OpenCrosshair"),
                T("A one-minute tour of this window. Skip it any time: “{0}” on the left brings it back.", T("Take the tour"))),
            new(T("Your crosshairs"), T("Everything you make is saved here. Click a crosshair to put it on screen."), Spot(() => _list)),
            new(T("Try it: add a ready-made one"), T("Open this list and pick any design. It's added to your crosshairs, ready to change."),
                Spot(() => Named("Presets")), Done: () => _s.Profiles.Count > profiles, Enter: () => profiles = _s.Profiles.Count),
            new(T("The preview"), T("This is how your crosshair looks. Every change is saved and shown on your screen right away."), Spot(() => _preview)),
            new(T("Try it: open a tab"), T("Settings are grouped in tabs. Click “{0}” to change the crosshair's lines.", T("Lines")),
                Spot(() => Named("Tabs")), Done: () => Tab(1).Checked, Enter: () => Tab(0).Checked = true),
            new(T("Try it: drag a slider"), T("Drag “{0}” and watch the preview. Rest the mouse on any setting for a tip about it.", T("Length")),
                Spot(() => Named(nameof(CrosshairStyle.Length))), Done: () => _s.Active.Length != length,
                Enter: () => { Tab(1).Checked = true; length = _s.Active.Length; }),
            new(T("Share with friends"),
                T("“{0}” puts a code for this crosshair on the clipboard. Paste a friend's code here and press “{1}” to get theirs.", T("Copy mine"), T("Import")),
                Spot(() => _code)),
            new(T("The community library"), T("Browse crosshairs other players shared, or share yours."), Spot(() => Named("Library"))),
            new(T("Hotkeys"), T("These work while you play, even with this window closed. Change them in the “{0}” tab.", T("Hotkeys")), Spot(() => _hotkeyHint)),
            new(T("You're all set"), T("Close this window whenever you like: your crosshair stays on screen, and OpenCrosshair waits in the tray by the clock.")),
        ];
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

    private void AddNew() => AddAndSelect(new CrosshairStyle { Name = T("Crosshair {0}", _s.Profiles.Count + 1) });

    private void Duplicate()
    {
        var copy = _s.Active.Clone();
        copy.Name = T("{0} copy", copy.Name);
        copy.Clamp();
        AddAndSelect(copy);
    }

    private void Delete()
    {
        if (_s.Profiles.Count == 1)
        {
            MessageBox.Show(this, T("Keep at least one crosshair."), Text);
            return;
        }
        if (MessageBox.Show(this, T("Delete “{0}”?", _s.Active.Name), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
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
            MessageBox.Show(this, T("That isn't a valid OpenCrosshair share code (it should start with OC2: or OC1:)."), Text,
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
            MessageBox.Show(this, T("Couldn't save the file: {0}", e.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        using var dialog = new SaveFileDialog { Filter = T("PNG image") + "|*.png", FileName = _s.Active.Name + ".png" };
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
            var note = T("{0}× zoom", zoom);
            if (_sprite.ImageMissing) note += "   ·   " + T("picture not found or unreadable, showing the drawn crosshair");
            TextRenderer.DrawText(e.Graphics, note, Font, new Point(8, 8), Color.Gainsboro);
        }
    }
}
