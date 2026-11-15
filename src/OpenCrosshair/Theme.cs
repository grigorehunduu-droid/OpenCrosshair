using Microsoft.Win32;

namespace OpenCrosshair;

/// <summary>
/// Light, dark or Windows-matching colors. Dark mode is applied by the app itself (WinForms' own dark mode
/// is still experimental and didn't reliably reach every window), so it works the same on every PC.
/// </summary>
internal static class Theme
{
    public static bool IsDark { get; private set; }

    public static Color Back => IsDark ? Color.FromArgb(30, 31, 34) : SystemColors.Control;
    public static Color Field => IsDark ? Color.FromArgb(43, 45, 49) : SystemColors.Window;
    public static Color ButtonBack => IsDark ? Color.FromArgb(52, 55, 60) : SystemColors.Control;
    public static Color Border => IsDark ? Color.FromArgb(78, 81, 87) : SystemColors.ControlDark;
    public static Color Text => IsDark ? Color.FromArgb(232, 232, 232) : SystemColors.ControlText;
    public static Color Subtle => IsDark ? Color.FromArgb(150, 153, 158) : SystemColors.GrayText;
    public static Color Accent => IsDark ? Color.FromArgb(45, 110, 200) : SystemColors.Highlight;
    public static Color MenuText => IsDark ? Text : SystemColors.MenuText;
    public static Color MenuBack => IsDark ? Color.FromArgb(32, 33, 36) : SystemColors.Menu;

    /// <summary>Picks the colors for windows created from now on (the designer is reopened when the theme changes).</summary>
    public static void Apply(AppTheme theme)
    {
        IsDark = theme == AppTheme.Dark || (theme == AppTheme.System && WindowsAppsAreDark());
        ToolStripManager.Renderer = IsDark ? new ToolStripProfessionalRenderer(new DarkMenuColors()) : new ToolStripProfessionalRenderer();
    }

    private static bool WindowsAppsAreDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 0;
    }

    /// <summary>Colors a whole window in the dark theme. Controls tagged "keep" (color swatches, the preview) keep their colors.</summary>
    public static void Style(Control root)
    {
        if (!IsDark) return;
        if (root is Form form) DarkTitleBar(form);
        foreach (var c in Descendants(root).Prepend(root))
        {
            if (c is ListBox or TextBox or ComboBox or ScrollableControl { AutoScroll: true }) DarkScrollbars(c);
            if (c.Tag is "keep") continue;
            switch (c)
            {
                case TextBox or ListBox:
                    c.BackColor = Field;
                    c.ForeColor = Text;
                    break;
                case ComboBox box:
                    box.FlatStyle = FlatStyle.Flat;
                    c.BackColor = Field;
                    c.ForeColor = Text;
                    break;
                case ButtonBase button when button is Button || button is RadioButton { Appearance: Appearance.Button }:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    c.BackColor = ButtonBack;
                    c.ForeColor = Text;
                    break;
                case LinkLabel link:
                    link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = Color.FromArgb(110, 170, 255);
                    c.BackColor = Back;
                    break;
                case CheckBox box:
                    box.FlatStyle = FlatStyle.Flat;
                    c.BackColor = Back;
                    c.ForeColor = Text;
                    break;
                default:
                    c.BackColor = Back;
                    c.ForeColor = c.Tag is "subtle" ? Subtle : Text;
                    break;
            }
        }
    }

    /// <summary>Tooltips are drawn by the app in the dark theme (Windows only draws them light).</summary>
    public static void Style(ToolTip tips)
    {
        if (!IsDark) return;
        tips.OwnerDraw = true;
        tips.Draw += (_, e) =>
        {
            using var back = new SolidBrush(Field);
            using var border = new Pen(Border);
            e.Graphics.FillRectangle(back, e.Bounds);
            e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, e.Bounds, Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    /// <summary>Windows' own dark style for scrollbars (and drop-down lists); the rest of the control keeps the app's colors.</summary>
    private static void DarkScrollbars(Control c)
    {
        string style = c is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer";
        void Apply() => Native.SetWindowTheme(c.Handle, style, null);
        if (c.IsHandleCreated) Apply();
        else c.HandleCreated += (_, _) => Apply();
    }

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));

    private static void DarkTitleBar(Form form)
    {
        void Apply()
        {
            int on = 1;
            Native.DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }
        if (form.IsHandleCreated) Apply();
        else form.HandleCreated += (_, _) => Apply();
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        private static readonly Color Back = Color.FromArgb(32, 33, 36), Hover = Color.FromArgb(62, 65, 71),
            Line = Color.FromArgb(72, 75, 80), Check = Color.FromArgb(45, 95, 165);

        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Check;
        public override Color CheckSelectedBackground => Check;
        public override Color CheckPressedBackground => Check;
    }
}
