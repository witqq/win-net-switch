namespace WinNetSwitch.Windows;

public static class InstallationPaths
{
    public const string ApplicationName = "WinNetSwitch";
    public const string ScheduledTaskName = "WinNetSwitch";
    public const string Version = "1.2.1";

    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        ApplicationName);

    public static string ApplicationPath { get; } = Path.Combine(
        InstallDirectory,
        "WinNetSwitch.exe");

    public static string ShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        "WinNetSwitch.lnk");

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationName);

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");
}
