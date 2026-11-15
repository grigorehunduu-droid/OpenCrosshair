using System.Diagnostics;
using System.Text;

namespace OpenCrosshair;

/// <summary>
/// Reports when another program's window comes to the front, which program it is, and whether it looks like a game.
/// Uses a Windows event hook: it learns which window is in front, nothing about what happens inside it.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Native.WinEventProc _callback;   // kept in a field: Windows calls it long after the constructor
    private readonly IntPtr _hook;
    private IntPtr _lastWindow;
    private string? _lastExe;

    public event Action<IntPtr>? Changed;

    public ForegroundWatcher()
    {
        _callback = (_, _, hwnd, _, _, _, _) => Changed?.Invoke(hwnd);
        _hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback,
            0, 0, Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>Program file name of a window, lowercase, e.g. "cs2.exe"; null if it can't be found out.</summary>
    public string? ExeOf(IntPtr hwnd)
    {
        if (hwnd == _lastWindow) return _lastExe;   // asked many times a second while Shoot is active
        _lastWindow = hwnd;
        _lastExe = null;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            _lastExe = process.ProcessName.ToLowerInvariant() + ".exe";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        return _lastExe;
    }

    /// <summary>
    /// The window in front is a game if it's linked to one of your crosshairs, or if it covers its whole monitor
    /// (how borderless-fullscreen games look). The desktop and taskbar never count.
    /// </summary>
    public bool GameInFront(Func<string, bool> isLinked)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsShell(hwnd)) return false;
        if (ExeOf(hwnd) is { } exe && isLinked(exe)) return true;
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var screen = Screen.FromHandle(hwnd).Bounds;
        return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
    }

    private static bool IsShell(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        Native.GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    public void Dispose() => Native.UnhookWinEvent(_hook);
}
