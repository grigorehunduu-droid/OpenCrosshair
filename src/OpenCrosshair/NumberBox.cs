using System.ComponentModel;
using System.Globalization;

namespace OpenCrosshair;

/// <summary>
/// A small box for a whole number, next to each slider. A plain text box (not NumericUpDown, whose arrow buttons
/// can't follow the dark theme): only digits can be typed, and the Up/Down keys step the value.
/// </summary>
internal sealed class NumberBox : TextBox
{
    private int _value;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Minimum { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Maximum { get; set; } = 100;
    public event EventHandler? ValueChanged;

    public NumberBox()
    {
        TextAlign = HorizontalAlignment.Right;
        Text = "0";
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            bool changed = value != _value;
            _value = value;
            Text = value.ToString(CultureInfo.InvariantCulture);
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar)) e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            Value += e.KeyCode == Keys.Up ? 1 : -1;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Typed numbers apply as soon as they're in range.</summary>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (int.TryParse(Text, NumberStyles.None, CultureInfo.InvariantCulture, out int typed)
            && typed >= Minimum && typed <= Maximum && typed != _value)
        {
            _value = typed;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>On leaving, anything empty or out of range snaps back to a valid number.</summary>
    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Value = int.TryParse(Text, NumberStyles.None, CultureInfo.InvariantCulture, out int typed) ? typed : _value;
    }
}
