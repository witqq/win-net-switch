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
}
