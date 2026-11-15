using System.Diagnostics;

namespace OpenCrosshair;

/// <summary>Lists the programs that have a window open, to pick the game a crosshair is for.</summary>
internal sealed class GamePicker : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    private readonly List<string> _exes = [];

    /// <summary>The chosen program's file name, e.g. "cs2.exe".</summary>
    public string? Chosen => _list.SelectedIndex >= 0 ? _exes[_list.SelectedIndex] : null;

    public GamePicker()
    {
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Link a running game";
        Icon = AppIcon.Large;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(480, 400);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        Padding = new Padding(10);

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 46, Tag = "subtle", ForeColor = Theme.Subtle,
            Text = "Start your game first, then pick it here. Whenever it comes to the front, this crosshair is switched on.",
        };
        var link = new Button { Text = "Link", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.AddRange([cancel, link]);
        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(hint);
        AcceptButton = link;
        CancelButton = cancel;
        _list.SelectedIndexChanged += (_, _) => link.Enabled = Chosen is not null;
        _list.DoubleClick += (_, _) =>
        {
            if (Chosen is not null) DialogResult = DialogResult.OK;
        };

        int self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                    var exe = process.ProcessName.ToLowerInvariant() + ".exe";
                    if (_exes.Contains(exe)) continue;
                    _exes.Add(exe);
                    _list.Items.Add($"{process.MainWindowTitle}   ({exe})");
                }
                catch (InvalidOperationException) { }   // the program closed while the list was being made
            }
        }
        Theme.Style(this);
    }
}
