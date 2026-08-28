using WinNetSwitch.Core;
using WinNetSwitch.Core.PowerShell;

namespace WinNetSwitch.Tests;

internal static class TestProgram
{
    private static readonly Guid WifiId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EthernetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UsbEthernetId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    internal static async Task<int> RunAsync()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("GetPhysicalAdapters parses PowerShell JSON", GetPhysicalAdaptersParsesJsonAsync),
            ("Malformed PowerShell JSON is rejected", MalformedJsonIsRejectedAsync),
            ("Mutation scripts use GUID and never adapter names", MutationScriptsUseGuidAsync),
            ("Switch enables target before disabling other adapters", SwitchOrdersMutationsAsync),
            ("Already exclusive target performs no mutation", AlreadyExclusiveTargetIsNoOpAsync),
            ("Missing target is rejected before mutations", MissingTargetIsRejectedAsync),
            ("Multiple enabled competitors are all disabled", MultipleCompetitorsAreDisabledAsync),
            ("Failed target enable leaves other adapters enabled", FailedEnablePreservesNetworkAsync),
            ("Unconfirmed target enable leaves other adapters enabled", UnconfirmedEnablePreservesNetworkAsync),
            ("Failed competitor disable restores initial state", FailedDisableRestoresInitialStateAsync),
            ("Concurrent switches are serialized", ConcurrentSwitchesAreSerializedAsync),
        };

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures.Add($"FAIL {test.Name}: {exception.Message}");
            }
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task GetPhysicalAdaptersParsesJsonAsync()
    {
        var runner = CreateRunner(wifiEnabled: true, ethernetEnabled: false);
        using var service = CreateService(runner);

        var adapters = await service.GetPhysicalAdaptersAsync();

        TestAssert.Equal(2, adapters.Count, "adapter count");
        var wifi = adapters.Single(adapter => adapter.Id == WifiId);
        var ethernet = adapters.Single(adapter => adapter.Id == EthernetId);
        TestAssert.Equal("Wi-Fi", wifi.Name, "Wi-Fi adapter name");
        TestAssert.True(wifi.IsEnabled, "Wi-Fi should be enabled");
        TestAssert.False(ethernet.IsEnabled, "Ethernet should be disabled");
    }

    private static async Task SwitchOrdersMutationsAsync()
    {
        var runner = CreateRunner(wifiEnabled: false, ethernetEnabled: true);
        using var service = CreateService(runner);

        var result = await service.SwitchExclusivelyAsync(WifiId);

        TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsEnabled, "Wi-Fi should be enabled");
        TestAssert.False(
            result.Single(adapter => adapter.Id == EthernetId).IsEnabled,
            "Ethernet should be disabled");
        var enablePosition = runner.Scripts.IndexOf(NetAdapterScripts.Enable(WifiId));
        var disablePosition = runner.Scripts.IndexOf(NetAdapterScripts.Disable(EthernetId));
        TestAssert.True(enablePosition >= 0, "target enable command should run");
        TestAssert.True(
            disablePosition > enablePosition,
            "the other adapter must be disabled only after the target was enabled");
    }

    private static async Task MalformedJsonIsRejectedAsync()
    {
        var runner = CreateRunner(wifiEnabled: true, ethernetEnabled: false);
        runner.ListOutputOverride = "not-json";
        using var service = CreateService(runner);

        var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
            () => service.GetPhysicalAdaptersAsync());

        TestAssert.Contains("Не удалось разобрать", exception.Message);
    }

    private static async Task MutationScriptsUseGuidAsync()
    {
        const string maliciousName = "Wi-Fi'; Disable-NetAdapter -Name *; #";
        var runner = new FakePowerShellRunner(
            CreateAdapter(WifiId, maliciousName, enabled: false),
            CreateAdapter(EthernetId, "Ethernet", enabled: true));
        using var service = CreateService(runner);

        await service.SwitchExclusivelyAsync(WifiId);

        var mutationScripts = runner.Scripts
            .Where(script => script != NetAdapterScripts.ListPhysicalAdapters)
            .ToArray();
        TestAssert.Equal(2, mutationScripts.Length, "mutation command count");
        foreach (var script in mutationScripts)
        {
            TestAssert.DoesNotContain(maliciousName, script);
            TestAssert.Contains("InterfaceGuid -eq $id", script);
            TestAssert.Contains("-Confirm:$false", script);
        }
        TestAssert.Contains(WifiId.ToString("D"), mutationScripts[0]);
        TestAssert.Contains(EthernetId.ToString("D"), mutationScripts[1]);
    }

    private static async Task AlreadyExclusiveTargetIsNoOpAsync()
    {
        var runner = CreateRunner(wifiEnabled: true, ethernetEnabled: false);
        using var service = CreateService(runner);

        var result = await service.SwitchExclusivelyAsync(WifiId);

        TestAssert.Equal(1, result.Count(adapter => adapter.IsEnabled), "enabled adapter count");
        TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsEnabled, "Wi-Fi should stay enabled");
        TestAssert.Equal(
            0,
            runner.Scripts.Count(script => script != NetAdapterScripts.ListPhysicalAdapters),
            "mutation command count");
    }

    private static async Task MultipleCompetitorsAreDisabledAsync()
    {
        var runner = new FakePowerShellRunner(
            CreateAdapter(WifiId, "Wi-Fi", enabled: true),
            CreateAdapter(EthernetId, "Ethernet", enabled: true),
            CreateAdapter(UsbEthernetId, "USB Ethernet", enabled: true));
        using var service = CreateService(runner);

        var result = await service.SwitchExclusivelyAsync(WifiId);

        TestAssert.Equal(1, result.Count(adapter => adapter.IsEnabled), "enabled adapter count");
        TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsEnabled, "Wi-Fi should be enabled");
        TestAssert.True(
            runner.Scripts.Contains(NetAdapterScripts.Disable(EthernetId)),
            "Ethernet disable command should run");
        TestAssert.True(
            runner.Scripts.Contains(NetAdapterScripts.Disable(UsbEthernetId)),
            "USB Ethernet disable command should run");
    }

    private static async Task MissingTargetIsRejectedAsync()
    {
        var runner = CreateRunner(wifiEnabled: true, ethernetEnabled: false);
        using var service = CreateService(runner);

        var missingId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
            () => service.SwitchExclusivelyAsync(missingId));

        TestAssert.Contains("больше не найден", exception.Message);
        TestAssert.Equal(
            0,
            runner.Scripts.Count(script => script != NetAdapterScripts.ListPhysicalAdapters),
            "mutation command count");
    }

    private static async Task FailedEnablePreservesNetworkAsync()
    {
        var runner = CreateRunner(wifiEnabled: false, ethernetEnabled: true);
        runner.FailEnableFor = WifiId;
        using var service = CreateService(runner);

        var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
            () => service.SwitchExclusivelyAsync(WifiId));

        TestAssert.Contains("Исходное состояние адаптеров восстановлено", exception.Message);
        TestAssert.False(runner.Adapters[WifiId].IsEnabled, "failed Wi-Fi enable must remain disabled");
        TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet must remain enabled");
        TestAssert.False(
            runner.Scripts.Contains(NetAdapterScripts.Disable(EthernetId)),
            "another adapter must not be disabled after target enable failed");
    }

    private static async Task FailedDisableRestoresInitialStateAsync()
    {
        var runner = new FakePowerShellRunner(
            CreateAdapter(WifiId, "Wi-Fi", enabled: false),
            CreateAdapter(EthernetId, "Ethernet", enabled: true),
            CreateAdapter(UsbEthernetId, "USB Ethernet", enabled: true));
        runner.FailDisableFor = UsbEthernetId;
        using var service = CreateService(runner);

        var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
            () => service.SwitchExclusivelyAsync(WifiId));

        TestAssert.Contains("Исходное состояние адаптеров восстановлено", exception.Message);
        TestAssert.False(runner.Adapters[WifiId].IsEnabled, "Wi-Fi should be rolled back to disabled");
        TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet should be restored");
        TestAssert.True(runner.Adapters[UsbEthernetId].IsEnabled, "USB Ethernet should remain enabled");
    }

    private static async Task UnconfirmedEnablePreservesNetworkAsync()
    {
        var runner = CreateRunner(wifiEnabled: false, ethernetEnabled: true);
        runner.IgnoreEnableFor = WifiId;
        using var service = CreateService(runner);

        var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
            () => service.SwitchExclusivelyAsync(WifiId));

        TestAssert.Contains("Windows не подтвердила включение", exception.Message);
        TestAssert.False(runner.Adapters[WifiId].IsEnabled, "Wi-Fi should remain disabled");
        TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet must remain enabled");
        TestAssert.False(
            runner.Scripts.Contains(NetAdapterScripts.Disable(EthernetId)),
            "another adapter must not be disabled before target enable is confirmed");
    }

    private static async Task ConcurrentSwitchesAreSerializedAsync()
    {
        var runner = CreateRunner(wifiEnabled: true, ethernetEnabled: false);
        runner.RunDelay = TimeSpan.FromMilliseconds(15);
        using var service = CreateService(runner);

        var switchToEthernet = service.SwitchExclusivelyAsync(EthernetId);
        await runner.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var switchToWifi = service.SwitchExclusivelyAsync(WifiId);
        await Task.WhenAll(switchToEthernet, switchToWifi);

        TestAssert.Equal(1, runner.MaximumConcurrentCalls, "maximum concurrent PowerShell calls");
        TestAssert.True(runner.Adapters[WifiId].IsEnabled, "second requested adapter should be enabled");
        TestAssert.False(runner.Adapters[EthernetId].IsEnabled, "first requested adapter should be disabled");
    }

    private static FakePowerShellRunner CreateRunner(bool wifiEnabled, bool ethernetEnabled) =>
        new(
            new PhysicalNetworkAdapter(
                WifiId,
                7,
                "Wi-Fi",
                "Wireless adapter",
                wifiEnabled ? "Up" : "Disabled",
                wifiEnabled ? "Connected" : "Unknown",
                "866.7 Mbps",
                wifiEnabled),
            new PhysicalNetworkAdapter(
                EthernetId,
                12,
                "Ethernet",
                "Wired adapter",
                ethernetEnabled ? "Up" : "Disabled",
                ethernetEnabled ? "Connected" : "Unknown",
                "1 Gbps",
                ethernetEnabled));

    private static PhysicalNetworkAdapterService CreateService(IPowerShellRunner runner) =>
        new(runner, verificationAttempts: 1, verificationDelay: TimeSpan.Zero);

    private static PhysicalNetworkAdapter CreateAdapter(Guid id, string name, bool enabled) =>
        new(
            id,
            id == WifiId ? 7 : id == EthernetId ? 12 : 18,
            name,
            $"{name} device",
            enabled ? "Up" : "Disabled",
            enabled ? "Connected" : "Unknown",
            id == WifiId ? "866.7 Mbps" : "1 Gbps",
            enabled);
}
