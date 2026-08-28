using System.Diagnostics;
using WinNetSwitch.Core;

namespace WinNetSwitch.App;

internal static class TraySmokeTest
{
    internal static int Run()
    {
        var service = new SmokeNetworkAdapterService();
        using var context = new TrayApplicationContext(service);
        using var timeoutTimer = new System.Windows.Forms.Timer { Interval = 50 };
        var stopwatch = Stopwatch.StartNew();
        var exitCode = 1;

        timeoutTimer.Tick += (_, _) =>
        {
            if (!context.InitialRefreshCompleted.IsCompleted)
            {
                if (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
                {
                    return;
                }

                context.ExitThread();
                return;
            }

            var menu = context.GetMenuSnapshot();
            var hasWifi = menu.Any(item => item.Text.StartsWith("Wi-Fi —", StringComparison.Ordinal) && item.Checked);
            var hasEthernet = menu.Any(
                item => item.Text.StartsWith("Ethernet —", StringComparison.Ordinal) && !item.Checked);
            var hasRefresh = menu.Any(item => item.Text == "Обновить" && item.Enabled);
            var hasExit = menu.Any(item => item.Text == "Выход" && item.Enabled);

            if (context.IsTrayIconVisible &&
                Application.OpenForms.Count == 0 &&
                hasWifi &&
                hasEthernet &&
                hasRefresh &&
                hasExit &&
                service.SwitchCalls == 0)
            {
                exitCode = 0;
            }

            context.ExitThread();
        };

        timeoutTimer.Start();
        Application.Run(context);
        return exitCode;
    }

    private sealed class SmokeNetworkAdapterService : INetworkAdapterService
    {
        private static readonly IReadOnlyList<PhysicalNetworkAdapter> Adapters =
        [
            new(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                7,
                "Wi-Fi",
                "Smoke wireless adapter",
                "Up",
                "Connected",
                "866.7 Mbps",
                IsEnabled: true),
            new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                12,
                "Ethernet",
                "Smoke wired adapter",
                "Disabled",
                "Unknown",
                "1 Gbps",
                IsEnabled: false),
        ];

        internal int SwitchCalls { get; private set; }

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> GetPhysicalAdaptersAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Adapters);

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> SwitchExclusivelyAsync(
            Guid targetAdapterId,
            CancellationToken cancellationToken = default)
        {
            SwitchCalls++;
            throw new InvalidOperationException("Smoke mode must never switch a real or fake adapter.");
        }
    }
}
