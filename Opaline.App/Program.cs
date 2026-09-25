using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Opaline.App.Services;

namespace Opaline.App;

/// <summary>
/// Custom entry so we can set MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY
/// before any Windows App SDK / WinUI type is touched (required for reliable
/// unpackaged self-contained launch).
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
            AppContext.BaseDirectory);

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            try
            {
                CrashLog.Write("Program.Main", ex);
                MessageBoxW(IntPtr.Zero,
                    "Opaline failed to start:\n\n" + ex.Message +
                    "\n\nSee crash.log next to Opaline.App.exe",
                    "Opaline",
                    0x00000010 /* MB_ICONERROR */);
            }
            catch { /* ignore */ }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
