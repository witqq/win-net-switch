using System.Reflection;
using WinNetSwitch.Windows;

namespace WinNetSwitch.Setup;

internal static class Program
{
    private const string PayloadResourceName = "WinNetSwitch.Payload.exe";
    private static readonly string SetupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinNetSwitch",
        "setup.log");

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
        try
        {
            WriteLog($"Setup started. Arguments: {string.Join(' ', args)}");
            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                using var payload = OpenPayload();
                return payload.Length > 0 ? 0 : 2;
            }

            if (!silent && MessageBox.Show(
                    "Install WinNetSwitch and start it automatically when you sign in to Windows?",
                    "Install WinNetSwitch",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return 0;
            }

            var temporaryPayload = Path.Combine(
                Path.GetTempPath(),
                $"WinNetSwitch-Payload-{Guid.NewGuid():N}.exe");
            try
            {
                using (var payload = OpenPayload())
                using (var destination = File.Create(temporaryPayload))
                {
                    payload.CopyTo(destination);
                }

                InstallationManager.Install(temporaryPayload, startAfterInstall: true);
                WriteLog("Installation manager completed successfully.");
            }
            finally
            {
                if (File.Exists(temporaryPayload))
                {
                    File.Delete(temporaryPayload);
                }
            }

            if (!silent)
            {
                MessageBox.Show(
                    "WinNetSwitch has been installed, added to startup, and launched in the system tray.",
                    "Installation complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return 0;
        }
        catch (Exception exception)
        {
            WriteLog($"Setup failed.{Environment.NewLine}{exception}");
            if (!silent)
            {
                MessageBox.Show(
                    $"Installation failed: {exception.Message}",
                    "Installation error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return 1;
        }
    }

    private static Stream OpenPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
        ?? throw new InvalidOperationException(
            "The installer does not contain WinNetSwitch.exe. Build it through scripts/publish.ps1.");

    private static void WriteLog(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(SetupLogPath)
                ?? throw new InvalidOperationException("Setup log path has no directory.");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                SetupLogPath,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
