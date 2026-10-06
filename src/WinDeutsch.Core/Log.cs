using System.Globalization;

namespace WinDeutsch.Core;

public static class Log
{
    private static readonly object Gate = new();

    // Appends to AppPaths.LogFile; never throws (logging must not take the app down).
    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            var stamp = TimeProvider.System.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            var entry = $"{stamp} ERROR {message}{Environment.NewLine}" +
                        (ex is null ? "" : ex + Environment.NewLine);
            lock (Gate)
                File.AppendAllText(AppPaths.LogFile, entry);
        }
        catch
        {
            // Nowhere left to report to.
        }
    }
}
