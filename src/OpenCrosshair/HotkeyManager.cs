namespace OpenCrosshair;

/// <summary>
/// Global hotkeys through RegisterHotKey: Windows tells us only about our own key combinations,
/// so the app never sees any other keystrokes (no keyboard hook).
/// </summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager() => CreateHandle(new CreateParams());

    /// <summary>Parses text like "Ctrl+Alt+H" or "Shift+F8".</summary>
    public static bool TryParse(string? text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win": modifiers |= Native.MOD_WIN; break;
                default: return false;
            }
        }
        var last = parts[^1];
        return !last.All(char.IsDigit) && Enum.TryParse(last, ignoreCase: true, out key)
            && Enum.IsDefined(key) && key != Keys.None && (key & Keys.Modifiers) == 0;
    }

    public bool Register(string? combo, Action action, bool repeat)
    {
        if (!TryParse(combo, out var mods, out var key)) return false;
        int id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, mods | (repeat ? 0 : Native.MOD_NOREPEAT), (uint)key)) return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action)) action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
