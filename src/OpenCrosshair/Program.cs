namespace OpenCrosshair;

internal static class Program
{
    private const string OpenSignalName = @"Local\OpenCrosshair.Open";

    /// <summary>Started by Windows at sign-in ("Start with Windows"): stay in the tray instead of opening the window.</summary>
    public const string TrayArgument = "--tray";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\OpenCrosshair.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            // Already running, e.g. the taskbar pin was clicked again: ask that copy to open its customize window.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(OpenSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                MessageBox.Show("OpenCrosshair is already running. Look for its icon in the system tray.", "OpenCrosshair");
            }
            return;
        }
        using var openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSignalName);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(openSignal, openDesigner: !args.Contains(TrayArgument)));
    }
}
