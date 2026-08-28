using WinNetSwitch.Core;
using WinNetSwitch.Core.PowerShell;

namespace WinNetSwitch.Tests;

internal static class TestProgram
{
    private static readonly Guid WifiId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EthernetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static async Task<int> RunAsync()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Adapter list includes Wi-Fi radio state", AdapterListIncludesRadioStateAsync),
            ("Malformed PowerShell JSON is rejected", MalformedJsonIsRejectedAsync),
            ("Mutation scripts use typed GUID and never adapter names", MutationScriptsUseGuidAsync),
            ("PnP mutation encodes device instance ID", PnpMutationEncodesDeviceIdAsync),
            ("Enabling Ethernet leaves Wi-Fi unchanged", EnablingEthernetLeavesWifiUnchangedAsync),
            ("Disabling Ethernet leaves Wi-Fi unchanged", DisablingEthernetLeavesWifiUnchangedAsync),
            ("Enabled Wi-Fi adapter turns software radio on", EnabledWifiTurnsRadioOnAsync),
            ("Disabled Wi-Fi adapter enables adapter then radio", DisabledWifiEnablesAdapterThenRadioAsync),
            ("Disabling Wi-Fi turns radio off before adapter", DisablingWifiOrdersOperationsAsync),
            ("Partial radio failure restores previous Wi-Fi radio state", RadioFailureRollsBackAsync),
            ("Missing target is rejected before mutations", MissingTargetIsRejectedAsync),
            ("Concurrent toggles are serialized", ConcurrentTogglesAreSerializedAsync),
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

    private static async Task AdapterListIncludesRadioStateAsync()
    {
        var (runner, radio, service) = CreateService(
            wifiAdapterEnabled: true,
            wifiRadioEnabled: false,
            ethernetEnabled: true);
        using (service)
        {
            var adapters = await service.GetPhysicalAdaptersAsync();
            var wifi = adapters.Single(adapter => adapter.Id == WifiId);
            var ethernet = adapters.Single(adapter => adapter.Id == EthernetId);

            TestAssert.True(wifi.IsWireless, "Wi-Fi should be identified as wireless");
            TestAssert.False(wifi.IsActive, "Wi-Fi should be inactive while software radio is off");
            TestAssert.True(ethernet.IsActive, "Ethernet should be active");
            TestAssert.Equal(0, radio.SetCalls.Count, "radio set call count");
            TestAssert.Equal(2, runner.Adapters.Count, "raw adapter count");
        }
    }

    private static async Task MalformedJsonIsRejectedAsync()
    {
        var (runner, _, service) = CreateService(true, true, true);
        runner.ListOutputOverride = "not-json";
        using (service)
        {
            var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
                () => service.GetPhysicalAdaptersAsync());
            TestAssert.Contains("Не удалось разобрать", exception.Message);
        }
    }

    private static async Task MutationScriptsUseGuidAsync()
    {
        const string maliciousName = "Ethernet'; Disable-NetAdapter -Name *; #";
        var runner = new FakePowerShellRunner(
            CreateAdapter(EthernetId, maliciousName, enabled: false) with { DeviceInstanceId = null });
        var radio = new FakeWirelessRadioController();
        using var service = CreateService(runner, radio);

        await service.SetAdapterEnabledAsync(EthernetId, enabled: true);

        var mutation = runner.Scripts.Single(script => script != NetAdapterScripts.ListPhysicalAdapters);
        TestAssert.DoesNotContain(maliciousName, mutation);
        TestAssert.Contains("[Guid]$_.InterfaceGuid -eq $id", mutation);
        TestAssert.Contains("-Confirm:$false", mutation);
        TestAssert.Contains(EthernetId.ToString("D"), mutation);
    }

    private static async Task EnablingEthernetLeavesWifiUnchangedAsync()
    {
        var (runner, radio, service) = CreateService(true, true, false);
        using (service)
        {
            var result = await service.SetAdapterEnabledAsync(EthernetId, enabled: true);

            TestAssert.True(result.Single(adapter => adapter.Id == EthernetId).IsActive, "Ethernet should be on");
            TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should stay on");
            TestAssert.False(
                runner.Scripts.Contains(NetAdapterScripts.Disable(WifiId)),
                "Wi-Fi must not be disabled");
            TestAssert.Equal(0, radio.SetCalls.Count, "radio set call count");
        }
    }

    private static async Task PnpMutationEncodesDeviceIdAsync()
    {
        const string maliciousDeviceId = "PCI\\VEN_TEST'; Disable-PnpDevice -InstanceId '*'; #";
        var adapter = CreateAdapter(WifiId, "Wi-Fi", enabled: false) with
        {
            DeviceInstanceId = maliciousDeviceId,
        };
        var runner = new FakePowerShellRunner(adapter);
        var radio = new FakeWirelessRadioController(
            null,
            (WifiId, new WirelessRadioState(false, true, 1)));
        using var service = CreateService(runner, radio);

        await service.SetAdapterEnabledAsync(WifiId, enabled: true);

        var mutation = runner.Scripts.Single(script => script != NetAdapterScripts.ListPhysicalAdapters);
        TestAssert.DoesNotContain(maliciousDeviceId, mutation);
        TestAssert.Contains("FromBase64String", mutation);
        TestAssert.Contains("Enable-PnpDevice", mutation);
    }

    private static async Task DisablingEthernetLeavesWifiUnchangedAsync()
    {
        var (runner, radio, service) = CreateService(true, true, true);
        using (service)
        {
            var result = await service.SetAdapterEnabledAsync(EthernetId, enabled: false);

            TestAssert.False(result.Single(adapter => adapter.Id == EthernetId).IsActive, "Ethernet should be off");
            TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should stay on");
            TestAssert.False(
                runner.Scripts.Contains(NetAdapterScripts.Disable(WifiId)),
                "Wi-Fi must not be disabled");
            TestAssert.Equal(0, radio.SetCalls.Count, "radio set call count");
        }
    }

    private static async Task EnabledWifiTurnsRadioOnAsync()
    {
        var (runner, radio, service) = CreateService(true, false, true);
        using (service)
        {
            var result = await service.SetAdapterEnabledAsync(WifiId, enabled: true);

            TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should be active");
            TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet should stay enabled");
            TestAssert.True(runner.Adapters[WifiId].IsEnabled, "Wi-Fi adapter should stay enabled");
            TestAssert.True(
                radio.SetCalls.Contains((WifiId, true)),
                "software radio should be turned on");
            TestAssert.Equal(
                0,
                runner.Scripts.Count(script => script != NetAdapterScripts.ListPhysicalAdapters),
                "PowerShell mutation count");
        }
    }

    private static async Task DisabledWifiEnablesAdapterThenRadioAsync()
    {
        var operations = new List<string>();
        var (runner, radio, service) = CreateService(false, false, true, operations);
        using (service)
        {
            var result = await service.SetAdapterEnabledAsync(WifiId, enabled: true);

            TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should be active");
            TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet should stay enabled");
            var enableIndex = operations.IndexOf($"powershell:enable:{WifiId:D}");
            var radioIndex = operations.IndexOf($"radio:set:{WifiId:D}:True");
            TestAssert.True(enableIndex >= 0, "adapter enable should run");
            TestAssert.True(radioIndex > enableIndex, "radio should be enabled after the adapter");
            TestAssert.True(radio.States[WifiId].IsOn, "software radio should be on");
        }
    }

    private static async Task DisablingWifiOrdersOperationsAsync()
    {
        var operations = new List<string>();
        var (runner, _, service) = CreateService(true, true, true, operations);
        using (service)
        {
            var result = await service.SetAdapterEnabledAsync(WifiId, enabled: false);

            TestAssert.False(result.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should be off");
            TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet should stay enabled");
            var radioIndex = operations.IndexOf($"radio:set:{WifiId:D}:False");
            var disableIndex = operations.IndexOf($"powershell:disable:{WifiId:D}");
            TestAssert.True(radioIndex >= 0, "radio disable should run");
            TestAssert.True(disableIndex > radioIndex, "adapter should be disabled after radio");
        }
    }

    private static async Task RadioFailureRollsBackAsync()
    {
        var (runner, radio, service) = CreateService(true, false, true);
        radio.FailSetFor = WifiId;
        radio.MutateBeforeFailure = true;
        radio.FailOnlyOnce = true;
        using (service)
        {
            var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
                () => service.SetAdapterEnabledAsync(WifiId, enabled: true));

            TestAssert.Contains("Исходное состояние адаптера восстановлено", exception.Message);
            TestAssert.True(runner.Adapters[WifiId].IsEnabled, "Wi-Fi adapter should keep its initial state");
            TestAssert.False(radio.States[WifiId].SoftwareOn, "Wi-Fi radio should be rolled back to off");
            TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet should stay enabled");
        }
    }

    private static async Task MissingTargetIsRejectedAsync()
    {
        var (runner, _, service) = CreateService(true, true, true);
        using (service)
        {
            var missingId = Guid.Parse("99999999-9999-9999-9999-999999999999");
            var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
                () => service.SetAdapterEnabledAsync(missingId, enabled: true));

            TestAssert.Contains("больше не найден", exception.Message);
            TestAssert.Equal(
                0,
                runner.Scripts.Count(script => script != NetAdapterScripts.ListPhysicalAdapters),
                "PowerShell mutation count");
        }
    }

    private static async Task ConcurrentTogglesAreSerializedAsync()
    {
        var (runner, _, service) = CreateService(true, true, true);
        runner.RunDelay = TimeSpan.FromMilliseconds(15);
        using (service)
        {
            var disableEthernet = service.SetAdapterEnabledAsync(EthernetId, enabled: false);
            await runner.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var disableWifi = service.SetAdapterEnabledAsync(WifiId, enabled: false);
            await Task.WhenAll(disableEthernet, disableWifi);

            TestAssert.Equal(1, runner.MaximumConcurrentCalls, "maximum concurrent PowerShell calls");
            TestAssert.False(runner.Adapters[EthernetId].IsEnabled, "Ethernet should be disabled");
            TestAssert.False(runner.Adapters[WifiId].IsEnabled, "Wi-Fi should be disabled");
        }
    }

    private static (
        FakePowerShellRunner Runner,
        FakeWirelessRadioController Radio,
        PhysicalNetworkAdapterService Service) CreateService(
            bool wifiAdapterEnabled,
            bool wifiRadioEnabled,
            bool ethernetEnabled,
            List<string>? operations = null)
    {
        var runner = new FakePowerShellRunner(
            operations,
            CreateAdapter(WifiId, "Wi-Fi", wifiAdapterEnabled),
            CreateAdapter(EthernetId, "Ethernet", ethernetEnabled));
        var radio = new FakeWirelessRadioController(
            operations,
            (WifiId, new WirelessRadioState(wifiRadioEnabled, HardwareOn: true, PhysicalLayerCount: 1)));
        return (runner, radio, CreateService(runner, radio));
    }

    private static PhysicalNetworkAdapterService CreateService(
        IPowerShellRunner runner,
        IWirelessRadioController radio) =>
        new(
            runner,
            radio,
            verificationAttempts: 1,
            verificationDelay: TimeSpan.Zero);

    private static PhysicalNetworkAdapter CreateAdapter(Guid id, string name, bool enabled) =>
        new(
            id,
            id == WifiId
                ? "PCI\\VEN_8086&DEV_TEST"
                : "PCI\\VEN_10EC&DEV_TEST",
            id == WifiId ? 7 : 12,
            name,
            $"{name} device",
            enabled ? "Up" : "Disabled",
            enabled ? "Connected" : "Unknown",
            id == WifiId ? "866.7 Mbps" : "1 Gbps",
            enabled,
            WirelessRadio: null);
}
