namespace OpenCrosshair;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\OpenCrosshair.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("OpenCrosshair is already running. Look for its icon in the system tray.", "OpenCrosshair");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
