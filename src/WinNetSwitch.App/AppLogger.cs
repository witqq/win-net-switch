using System.Text;

namespace WinNetSwitch.App;

internal static class AppLogger
{
    private const long MaximumLogBytes = 1_048_576;
    private static readonly object Sync = new();
    private static readonly string PreferredLogDirectory = Path.Combine(
        GetLocalApplicationDataDirectory(),
        "WinNetSwitch",
        "logs");
    private static string _activeLogPath = Path.Combine(PreferredLogDirectory, "WinNetSwitch.log");

    internal static string LogPath
    {
        get
        {
            lock (Sync)
            {
                return _activeLogPath;
            }
        }
    }

    internal static bool EnsureCreated() => Write("INFO", "Logger initialized.");

    internal static void Info(string message) => _ = Write("INFO", message);

    internal static void Error(string message, Exception exception) =>
        _ = Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static bool Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                var preferredLogPath = Path.Combine(PreferredLogDirectory, "WinNetSwitch.log");
                Append(preferredLogPath, level, message);
                _activeLogPath = preferredLogPath;
                return true;
            }
            catch (Exception preferredException)
            {
                try
                {
                    var fallbackLogPath = Path.Combine(AppContext.BaseDirectory, "WinNetSwitch.log");
                    Append(
                        fallbackLogPath,
                        "WARN",
                        $"Preferred log path failed: {preferredException.Message}{Environment.NewLine}{message}");
                    _activeLogPath = fallbackLogPath;
                    return true;
                }
                catch
                {
                    // Logging must never make network switching or shutdown fail.
                    return false;
                }
            }
        }
    }

    private static void Append(string logPath, string level, string message)
    {
        var logDirectory = Path.GetDirectoryName(logPath)
            ?? throw new InvalidOperationException("The log path has no directory.");
        Directory.CreateDirectory(logDirectory);
        RotateIfNeeded(logPath);
        var line = string.Concat(
            DateTimeOffset.Now.ToString("O"),
            " [",
            level,
            "] [PID ",
            Environment.ProcessId,
            "] [TID ",
            Environment.CurrentManagedThreadId,
            "] ",
            message,
            Environment.NewLine);
        File.AppendAllText(logPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void RotateIfNeeded(string logPath)
    {
        var logFile = new FileInfo(logPath);
        if (!logFile.Exists || logFile.Length < MaximumLogBytes)
        {
            return;
        }

        var logDirectory = Path.GetDirectoryName(logPath)
            ?? throw new InvalidOperationException("The log path has no directory.");
        var previousLogPath = Path.Combine(logDirectory, "WinNetSwitch.previous.log");
        File.Move(logPath, previousLogPath, overwrite: true);
    }

    private static string GetLocalApplicationDataDirectory()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var fromSpecialFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(fromSpecialFolder)
            ? AppContext.BaseDirectory
            : fromSpecialFolder;
    }
}
