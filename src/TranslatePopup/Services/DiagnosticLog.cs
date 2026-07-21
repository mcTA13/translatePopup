using System.IO;

namespace TranslatePopup.Services;

/// <summary>Temporary diagnostic logger for tracking down the intermittent "translate button
/// doesn't reopen the window" issue. Writes to %AppData%\TranslatePopup\logs\debug.log, truncated
/// fresh on each app start so a single log always covers one reproduction session.</summary>
internal static class DiagnosticLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TranslatePopup", "logs", "debug.log");

    private static readonly object Lock = new();

    static DiagnosticLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, $"=== TranslatePopup session started {DateTime.Now:O} ==={Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break the app.
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
