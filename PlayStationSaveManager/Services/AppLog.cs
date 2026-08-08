using System;
using System.IO;
using System.Text;

namespace PlayStationSaveManager.Services;

public static class AppLog
{
    private static readonly object Sync = new();

    public static string LogsDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "PlayStationSaveManager",
            "Logs");

    public static void WriteActivity(string message)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);

            var path =
                Path.Combine(
                    LogsDirectory,
                    $"activity-{DateTime.Now:yyyy-MM-dd}.log");

            AppendLine(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }
        catch
        {
            // Logging must never interfere with the application.
        }
    }

    public static void WriteCrash(
        Exception exception,
        string source)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);

            var path =
                Path.Combine(
                    LogsDirectory,
                    "crash.log");

            var builder = new StringBuilder();
            builder.AppendLine(
                "============================================================");
            builder.AppendLine(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Unhandled exception");
            builder.AppendLine(
                $"Source: {source}");
            builder.AppendLine(
                $"App version: {typeof(AppLog).Assembly.GetName().Version}");
            builder.AppendLine(
                $"OS: {Environment.OSVersion}");
            builder.AppendLine(
                $".NET: {Environment.Version}");
            builder.AppendLine();
            builder.AppendLine(exception.ToString());
            builder.AppendLine();

            lock (Sync)
            {
                File.AppendAllText(
                    path,
                    builder.ToString(),
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Never mask the original crash with a logging failure.
        }
    }

    private static void AppendLine(
        string path,
        string line)
    {
        lock (Sync)
        {
            File.AppendAllText(
                path,
                line + Environment.NewLine,
                Encoding.UTF8);
        }
    }
}
