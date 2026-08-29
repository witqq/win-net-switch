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
        var refreshStarted = false;

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
            var wifi = menu.SingleOrDefault(
                item => item.Text.StartsWith("Wi-Fi —", StringComparison.Ordinal) && item.Checked);
            var ethernet = menu.SingleOrDefault(
                item => item.Text.StartsWith("Ethernet —", StringComparison.Ordinal) && !item.Checked);
            var hasRefresh = menu.Any(item => item.Text == "Обновить" && item.Enabled);
            var hasExit = menu.Any(item => item.Text == "Выход" && item.Enabled);

            if (!refreshStarted)
            {
                service.BlockNextRefresh();
                context.BeginRefreshForSmoke();
                refreshStarted = true;
                return;
            }

            var refreshIsPending = !service.PendingRefreshCompleted;
            var hasRefreshStatus = menu.Any(item => item.Text == "Обновление списка…");
            var wifiActionsAvailable = wifi is not null &&
                wifi.Children.Any(item => item.Text == "Выключить" && item.Enabled) &&
                wifi.Children.Any(item => item.Text == "Включить только этот адаптер" && item.Enabled);
            var ethernetActionsAvailable = ethernet is not null &&
                ethernet.Children.Any(item => item.Text == "Включить" && item.Enabled) &&
                ethernet.Children.Any(item => item.Text == "Включить только этот адаптер" && item.Enabled);

            if (context.IsTrayIconVisible &&
                Application.OpenForms.Count == 0 &&
                refreshIsPending &&
                hasRefreshStatus &&
                wifiActionsAvailable &&
                ethernetActionsAvailable &&
                !hasRefresh &&
                hasExit &&
                service.SwitchCalls == 0)
            {
                exitCode = 0;
            }

            service.CompletePendingRefresh();
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
                null,
                7,
                "Wi-Fi",
                "Smoke wireless adapter",
                "Up",
                "Connected",
                "866.7 Mbps",
                IsEnabled: true,
                WirelessRadio: new WirelessRadioState(true, true, 1)),
            new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                null,
                12,
                "Ethernet",
                "Smoke wired adapter",
                "Disabled",
                "Unknown",
                "1 Gbps",
                IsEnabled: false,
                WirelessRadio: null),
        ];

        internal int SwitchCalls { get; private set; }

        private TaskCompletionSource<IReadOnlyList<PhysicalNetworkAdapter>>? _pendingRefresh;

        internal bool PendingRefreshCompleted => _pendingRefresh?.Task.IsCompleted ?? true;

        internal void BlockNextRefresh() =>
            _pendingRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void CompletePendingRefresh() => _pendingRefresh?.TrySetResult(Adapters);

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> GetPhysicalAdaptersAsync(
            CancellationToken cancellationToken = default)
        {
            var pendingRefresh = _pendingRefresh;
            return pendingRefresh is not null && !pendingRefresh.Task.IsCompleted
                ? pendingRefresh.Task.WaitAsync(cancellationToken)
                : Task.FromResult(Adapters);
        }

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> SetAdapterEnabledAsync(
            Guid targetAdapterId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            SwitchCalls++;
            throw new InvalidOperationException("Smoke mode must never switch a real or fake adapter.");
        }

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> EnableOnlyAsync(
            Guid targetAdapterId,
            CancellationToken cancellationToken = default)
        {
            SwitchCalls++;
            throw new InvalidOperationException("Smoke mode must never switch a real or fake adapter.");
        }
    }
}
