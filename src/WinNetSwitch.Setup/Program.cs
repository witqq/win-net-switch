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
                    "Установить WinNetSwitch и включить автозапуск при входе в Windows?",
                    "Установка WinNetSwitch",
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
                    "WinNetSwitch установлен, добавлен в автозапуск и запущен в системном трее.",
                    "Установка завершена",
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
                    $"Установка не завершена: {exception.Message}",
                    "Ошибка установки",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return 1;
        }
    }

    private static Stream OpenPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
        ?? throw new InvalidOperationException(
            "Установщик не содержит WinNetSwitch.exe. Соберите setup через scripts/publish.ps1.");

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
