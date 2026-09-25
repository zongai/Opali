using System.Diagnostics;
using System.Text;

namespace Opaline.App.Services;

/// <summary>Lightweight file log next to the exe (app.log).</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "app.log");

    public static void Info(string area, string message) => Write("INF", area, message);
    public static void Warn(string area, string message) => Write("WRN", area, message);
    public static void Error(string area, string message, Exception? ex = null)
    {
        Write("ERR", area, message + (ex is null ? "" : " | " + ex.GetType().Name + ": " + ex.Message));
        if (ex?.StackTrace is { } st)
            Write("ERR", area, st.Split('\n').FirstOrDefault()?.Trim() ?? "");
    }

    private static void Write(string level, string area, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {area}: {message}";
        try { Debug.WriteLine(line); } catch { /* ignore */ }
        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* ignore */ }
    }
}
