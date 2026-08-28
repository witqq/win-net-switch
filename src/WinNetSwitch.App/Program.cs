using WinNetSwitch.Core;
using WinNetSwitch.Core.PowerShell;

namespace WinNetSwitch.App;

internal static class Program
{
    private const string SingleInstanceMutexName = "Local\\WinNetSwitch.TrayApplication";

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ConfigureUnhandledExceptionLogging();
        AppLogger.EnsureCreated();
        AppLogger.Info($"Application starting. Mode: {GetMode(args)}.");

        try
        {
            return await RunAsync(args);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Fatal application error.", exception);
            if (args.Length == 0)
            {
                MessageBox.Show(
                    $"Критическая ошибка: {exception.Message}\n\nПодробности: {AppLogger.LogPath}",
                    "WinNetSwitch",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return 1;
        }
        finally
        {
            AppLogger.Info("Application stopped.");
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 &&
            string.Equals(args[0], "--smoke-test", StringComparison.OrdinalIgnoreCase))
        {
            return TraySmokeTest.Run();
        }

        if (args.Length == 1 &&
            string.Equals(args[0], "--probe-adapters", StringComparison.OrdinalIgnoreCase))
        {
            using var service = new PhysicalNetworkAdapterService(new PowerShellRunner());
            _ = await service.GetPhysicalAdaptersAsync();
            return 0;
        }

        if (args.Length == 1 &&
            string.Equals(args[0], "--logging-self-test", StringComparison.OrdinalIgnoreCase))
        {
            return AppLogger.EnsureCreated() && File.Exists(AppLogger.LogPath) ? 0 : 4;
        }

        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "WinNetSwitch уже запущен. Найдите его значок в системном трее.",
                "WinNetSwitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        using var context = new TrayApplicationContext();
        Application.Run(context);
        return 0;
    }

    private static void ConfigureUnhandledExceptionLogging()
    {
        Application.ThreadException += (_, eventArgs) =>
            AppLogger.Error("Unhandled Windows Forms thread exception.", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            AppLogger.Error(
                "Unhandled AppDomain exception.",
                eventArgs.ExceptionObject as Exception ??
                new InvalidOperationException(eventArgs.ExceptionObject?.ToString() ?? "Unknown exception"));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    private static string GetMode(string[] args) =>
        args.Length == 0 ? "tray" : string.Join(' ', args);
}
