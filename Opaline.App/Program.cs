using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Opaline.App;

/// <summary>
/// Entry point that logs before any WinRT/WinUI type is used.
/// Self-contained mode uses UndockedRegFreeWinRT (not system framework bootstrap).
/// </summary>
public static class Program
{
    private static readonly string LogFile =
        Path.Combine(AppContext.BaseDirectory, "crash.log");

    [STAThread]
    private static void Main(string[] args)
    {
        // 1) Prove managed Main was reached — pure BCL, no WinRT
        WriteLog("Main entered");
        WriteLog("BaseDirectory=" + AppContext.BaseDirectory);
        WriteLog("Exe=" + Environment.ProcessPath);
        WriteLog("OS=" + Environment.OSVersion);
        try
        {
            var dir = AppContext.BaseDirectory;
            WriteLog("Has Bootstrap.dll=" + File.Exists(Path.Combine(dir, "Microsoft.WindowsAppRuntime.Bootstrap.dll")));
            WriteLog("Has WindowsAppRuntime.dll=" + File.Exists(Path.Combine(dir, "Microsoft.WindowsAppRuntime.dll")));
            WriteLog("Has WinUI.dll=" + File.Exists(Path.Combine(dir, "Microsoft.WinUI.dll")));
            WriteLog("Has Bootstrap.Net.dll=" + File.Exists(Path.Combine(dir, "Microsoft.WindowsAppRuntime.Bootstrap.Net.dll")));
        }
        catch (Exception ex)
        {
            WriteLog("File probe failed: " + ex.Message);
        }

        // 2) Point undocked runtime at our folder (self-contained natives live here)
        try
        {
            Environment.SetEnvironmentVariable(
                "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
                AppContext.BaseDirectory);
            WriteLog("Set MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY");
        }
        catch (Exception ex)
        {
            WriteLog("Env set failed: " + ex.Message);
        }

        try
        {
            WriteLog("ComWrappersSupport.InitializeComWrappers…");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            WriteLog("Application.Start…");
            Application.Start(p =>
            {
                WriteLog("Application.Start callback");
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                WriteLog("new App()");
                _ = new App();
                WriteLog("App constructed");
            });
            WriteLog("Application.Start returned");
        }
        catch (Exception ex)
        {
            WriteLog("FATAL: " + ex.GetType().FullName + ": " + ex.Message);
            WriteLog(ex.StackTrace ?? "");
            if (ex.InnerException is not null)
            {
                WriteLog("Inner: " + ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message);
                WriteLog(ex.InnerException.StackTrace ?? "");
            }
            try
            {
                MessageBoxW(IntPtr.Zero,
                    "Opaline failed to start:\n\n" + ex.Message +
                    "\n\nSee crash.log next to Opaline.App.exe",
                    "Opaline",
                    0x00000010);
            }
            catch { /* ignore */ }
        }
    }

    private static void WriteLog(string line)
    {
        try
        {
            File.AppendAllText(LogFile,
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine,
                Encoding.UTF8);
        }
        catch { /* never throw */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
