using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace OpenCrosshair;

/// <summary>Crosshair list on the left, every setting in a PropertyGrid, live zoomed preview, share codes.</summary>
internal sealed class DesignerForm : Form
{
    private readonly AppSettings _s;
    private readonly Action _changed;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(48, 52, 60) };
    private readonly TextBox _code = new() { Dock = DockStyle.Fill, PlaceholderText = "Paste a share code here, then Import" };
    private bool _syncing;

    public DesignerForm(AppSettings settings, Action changed)
    {
        _s = settings;
        _changed = changed;
        Text = "OpenCrosshair designer";
        ClientSize = new Size(880, 540);
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340 };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_preview);

        var share = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 38, ColumnCount = 5, Padding = new Padding(4) };
        share.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        share.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        share.Controls.Add(new Label { Text = "Share code:", AutoSize = true, Anchor = AnchorStyles.Left });
        share.Controls.Add(_code);
        share.Controls.Add(Button("Copy mine", CopyCode));
        share.Controls.Add(Button("Import", ImportCode));
        share.Controls.Add(Button("Export PNG", ExportPng));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        buttons.Controls.AddRange([Button("New", AddNew), Button("Duplicate", Duplicate), Button("Delete", Delete)]);
        var left = new Panel { Dock = DockStyle.Left, Width = 210, Padding = new Padding(4) };
        left.Controls.Add(_list);
        left.Controls.Add(buttons);

        // Fill first, edges after: WinForms docks the last-added control first.
        Controls.Add(split);
        Controls.Add(share);
        Controls.Add(left);

        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing || _list.SelectedIndex < 0 || _list.SelectedIndex == _s.ActiveProfile) return;
            _s.ActiveProfile = _list.SelectedIndex;
            ShowActive();
            _changed();
        };
        _grid.PropertyValueChanged += (_, _) =>
        {
            _s.Active.Clamp();
            _grid.Refresh();
            _syncing = true;
            _list.Items[_s.ActiveProfile] = _s.Active.Name;
            _syncing = false;
            ShowActive();
            _changed();
        };
        _preview.Paint += DrawPreview;
        _preview.Resize += (_, _) => _preview.Invalidate();
        SyncFromSettings();
    }

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
        _grid.SelectedObject = _s.Active;
        _preview.Invalidate();
    }

    private void AddAndSelect(CrosshairStyle style)
    {
        _s.Profiles.Add(style);
        _s.ActiveProfile = _s.Profiles.Count - 1;
        SyncFromSettings();
        _changed();
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
        _changed();
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
            MessageBox.Show(this, "That isn't a valid OpenCrosshair share code (it should start with OC1:).", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        using var bmp = CrosshairRenderer.Render(_s.Active);
        int zoom = Math.Clamp(Math.Min(_preview.Width, _preview.Height) / bmp.Width, 1, 8);
        int size = bmp.Width * zoom;
        var dest = new Rectangle((_preview.Width - size) / 2, (_preview.Height - size) / 2, size, size);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = _s.Active.OpacityPercent / 100f });
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attributes);
        TextRenderer.DrawText(e.Graphics, $"{zoom}× zoom", Font, new Point(8, 8), Color.Gainsboro);
    }

    private static Button Button(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) => onClick();
        return b;
    }
}
