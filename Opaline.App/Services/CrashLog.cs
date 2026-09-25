using System.Diagnostics;
using System.Text;

namespace Opaline.App.Services;

/// <summary>Writes crash details next to Opaline.App.exe (AppContext.BaseDirectory).</summary>
public static class CrashLog
{
    public static string LogPath { get; } = Path.Combine(AppContext.BaseDirectory, "crash.log");

    public static void Write(string stage, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("======== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ========");
            sb.AppendLine("Stage: " + stage);
            sb.AppendLine("OS: " + Environment.OSVersion);
            sb.AppendLine("64bit: " + Environment.Is64BitProcess);
            sb.AppendLine("BaseDir: " + AppContext.BaseDirectory);
            if (ex is not null)
            {
                sb.AppendLine("Type: " + ex.GetType().FullName);
                sb.AppendLine("Message: " + ex.Message);
                sb.AppendLine(ex.StackTrace);
                if (ex.InnerException is not null)
                {
                    sb.AppendLine("Inner: " + ex.InnerException.GetType().FullName);
                    sb.AppendLine("InnerMsg: " + ex.InnerException.Message);
                    sb.AppendLine(ex.InnerException.StackTrace);
                }
            }
            sb.AppendLine();
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch
        {
            // never throw from logger
        }

        try { Debug.WriteLine($"[Opaline crash] {stage}: {ex}"); } catch { /* ignore */ }
    }
}
