using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
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
            ("Recent adapter snapshot avoids a redundant pre-switch query", RecentSnapshotSpeedsUpSwitchAsync),
            ("Toggle changes only the selected adapter", ToggleChangesOnlySelectedAdapterAsync),
            ("Enable-only disables every other adapter", EnableOnlyDisablesOtherAdaptersAsync),
            ("Enable-only failure restores the initial state", EnableOnlyFailureRollsBackAsync),
            ("Cycle switches exclusively with deterministic wraparound", CycleSwitchesWithWraparoundAsync),
            ("Windows control pipe is limited to the interactive logon", WindowsControlPipeIsLogonScopedAsync),
            ("Local control pipe serves bounded list, toggle, and cycle requests", LocalControlPipeServesRequestsAsync),
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
            TestAssert.Contains("Could not parse", exception.Message);
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
            TestAssert.Equal(
                2,
                runner.Scripts.Count(script => script == NetAdapterScripts.ListPhysicalAdapters),
                "adapter queries while enabling a disabled Wi-Fi adapter");

            _ = await service.SetAdapterEnabledAsync(WifiId, enabled: false);
            TestAssert.True(
                radio.SetCalls.Contains((WifiId, false)),
                "the verified radio state should be cached for the next switch");
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

            TestAssert.Contains("The original adapter state was restored", exception.Message);
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

            TestAssert.Contains("is no longer available", exception.Message);
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

    private static async Task RecentSnapshotSpeedsUpSwitchAsync()
    {
        var (runner, _, service) = CreateService(true, true, true);
        using (service)
        {
            _ = await service.GetPhysicalAdaptersAsync();
            runner.Scripts.Clear();

            _ = await service.SetAdapterEnabledAsync(EthernetId, enabled: false);

            TestAssert.Equal(
                1,
                runner.Scripts.Count(script => script == NetAdapterScripts.ListPhysicalAdapters),
                "adapter queries during a switch with a recent snapshot");
        }
    }

    private static async Task EnableOnlyDisablesOtherAdaptersAsync()
    {
        var (runner, _, service) = CreateService(true, true, true);
        using (service)
        {
            var result = await service.EnableOnlyAsync(WifiId);

            TestAssert.True(result.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should be active");
            TestAssert.False(
                result.Single(adapter => adapter.Id == EthernetId).IsEnabled,
                "Ethernet should be disabled");
            TestAssert.Equal(1, result.Count(adapter => adapter.IsEnabled), "enabled adapter count");
            TestAssert.Equal(
                3,
                runner.Scripts.Count(script => script == NetAdapterScripts.ListPhysicalAdapters),
                "adapter queries during enable-only");
        }
    }

    private static async Task ToggleChangesOnlySelectedAdapterAsync()
    {
        var (runner, radio, service) = CreateService(true, true, true);
        using (service)
        {
            var disabled = await service.ToggleAdapterAsync(WifiId);

            TestAssert.False(disabled.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should be off");
            TestAssert.True(
                disabled.Single(adapter => adapter.Id == EthernetId).IsActive,
                "Ethernet should remain on");

            var enabled = await service.ToggleAdapterAsync(WifiId);

            TestAssert.True(enabled.Single(adapter => adapter.Id == WifiId).IsActive, "Wi-Fi should be on");
            TestAssert.True(
                enabled.Single(adapter => adapter.Id == EthernetId).IsActive,
                "Ethernet should still remain on");
            TestAssert.True(radio.States[WifiId].SoftwareOn, "Wi-Fi radio should be restored to on");
            TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet must never be toggled");
        }
    }

    private static async Task EnableOnlyFailureRollsBackAsync()
    {
        var (runner, radio, service) = CreateService(true, true, true);
        runner.FailDisableFor = EthernetId;
        using (service)
        {
            var exception = await TestAssert.ThrowsAsync<NetworkSwitchException>(
                () => service.EnableOnlyAsync(WifiId));

            TestAssert.Contains("The original adapter states were restored", exception.Message);
            TestAssert.True(runner.Adapters[WifiId].IsEnabled, "Wi-Fi adapter should be restored");
            TestAssert.True(runner.Adapters[EthernetId].IsEnabled, "Ethernet should be restored");
            TestAssert.True(radio.States[WifiId].SoftwareOn, "Wi-Fi radio should be restored");
        }
    }

    private static async Task CycleSwitchesWithWraparoundAsync()
    {
        var (runner, _, service) = CreateService(true, true, false);
        using (service)
        {
            var ethernetOnly = await service.CycleToNextAsync();

            TestAssert.True(
                ethernetOnly.Single(adapter => adapter.Id == EthernetId).IsActive,
                "first cycle should wrap from Wi-Fi to alphabetically first Ethernet");
            TestAssert.False(
                ethernetOnly.Single(adapter => adapter.Id == WifiId).IsEnabled,
                "first cycle should disable Wi-Fi");
            TestAssert.Equal(1, ethernetOnly.Count(adapter => adapter.IsEnabled), "first enabled count");

            var wifiOnly = await service.CycleToNextAsync();

            TestAssert.True(
                wifiOnly.Single(adapter => adapter.Id == WifiId).IsActive,
                "second cycle should advance from Ethernet to Wi-Fi");
            TestAssert.False(
                wifiOnly.Single(adapter => adapter.Id == EthernetId).IsEnabled,
                "second cycle should disable Ethernet");
            TestAssert.Equal(1, wifiOnly.Count(adapter => adapter.IsEnabled), "second enabled count");
            TestAssert.True(runner.Adapters[WifiId].IsEnabled, "Wi-Fi should finish enabled");
        }
    }

    private static Task WindowsControlPipeIsLogonScopedAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User
            ?? throw new InvalidOperationException("The current user SID was not returned.");
        var testLogonSid = new SecurityIdentifier("S-1-5-5-123-456");
        var security = LocalControlPipeFactory.CreateWindowsSecurity(userSid, testLogonSid);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new InvalidOperationException("The pipe owner SID was not returned.");
        TestAssert.Equal(userSid.Value, owner.Value, "pipe owner SID");

        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToArray();
        TestAssert.Equal(1, rules.Length, "explicit pipe access rule count");
        var allowedSid = (SecurityIdentifier)rules[0].IdentityReference;
        TestAssert.Equal(testLogonSid.Value, allowedSid.Value, "pipe logon SID");
        TestAssert.Equal(AccessControlType.Allow, rules[0].AccessControlType, "pipe rule type");
        TestAssert.Equal(PipeAccessRights.FullControl, rules[0].PipeAccessRights, "pipe rights");

        var mandatoryDescriptor = LocalControlPipeFactory.CreateMediumIntegrityLabelDescriptor();
        TestAssert.Equal(1, mandatoryDescriptor.SystemAcl?.Count ?? 0, "mandatory label ACE count");
        var mandatoryAce = mandatoryDescriptor.SystemAcl![0];
        var mandatoryAceBytes = new byte[mandatoryAce.BinaryLength];
        mandatoryAce.GetBinaryForm(mandatoryAceBytes, 0);
        TestAssert.Equal((byte)0x11, mandatoryAceBytes[0], "mandatory ACE type");
        TestAssert.Equal(1, BitConverter.ToInt32(mandatoryAceBytes, 4), "NO_WRITE_UP mask");
        var mandatorySid = new SecurityIdentifier(mandatoryAceBytes, 8);
        TestAssert.True(
            mandatorySid.IsWellKnown(WellKnownSidType.WinMediumLabelSid),
            "mandatory label should use the medium-integrity SID");
        return Task.CompletedTask;
    }

    private static async Task LocalControlPipeServesRequestsAsync()
    {
        var (runner, _, service) = CreateService(true, true, false);
        var notifications = new List<NetworkControlNotification>();
        var serverErrors = new List<string>();
        using (service)
        using (var server = new NamedPipeControlServer(
                   service,
                   LocalControlPipeFactory.CreateForCurrentUserSmoke,
                   logError: (message, exception) =>
                       serverErrors.Add($"{message} {exception.Message}"),
                   notify: notifications.Add))
        {
            server.Start();

            using (var list = JsonDocument.Parse(
                       await SendControlRequestAsync("""{"version":1,"command":"list"}""")))
            {
                TestAssert.True(list.RootElement.GetProperty("ok").GetBoolean(), "list should succeed");
                TestAssert.Equal(
                    2,
                    list.RootElement.GetProperty("adapters").GetArrayLength(),
                    "pipe adapter count");
            }

            using (var toggle = JsonDocument.Parse(
                       await SendControlRequestAsync(
                           $$"""{"version":1,"command":"toggle","adapterId":"{{WifiId:D}}"}""")))
            {
                TestAssert.True(toggle.RootElement.GetProperty("ok").GetBoolean(), "toggle should succeed");
                TestAssert.False(runner.Adapters[WifiId].IsEnabled, "pipe toggle should disable Wi-Fi");
                TestAssert.False(
                    runner.Adapters[EthernetId].IsEnabled,
                    "pipe toggle must not enable Ethernet");
                TestAssert.Equal(1, notifications.Count, "toggle notification count");
                TestAssert.Contains("Wi-Fi", notifications[0].Message);
                TestAssert.False(notifications[0].IsError, "toggle notification should be successful");
            }

            using (var cycle = JsonDocument.Parse(
                       await SendControlRequestAsync("""{"version":1,"command":"cycle"}""")))
            {
                TestAssert.True(cycle.RootElement.GetProperty("ok").GetBoolean(), "cycle should succeed");
                TestAssert.True(
                    runner.Adapters[EthernetId].IsEnabled,
                    "pipe cycle should select alphabetically first Ethernet when none is active");
                TestAssert.False(runner.Adapters[WifiId].IsEnabled, "pipe cycle should leave Wi-Fi disabled");
                TestAssert.Equal(2, notifications.Count, "cycle notification count");
                TestAssert.Contains("Ethernet", notifications[1].Message);
                TestAssert.False(notifications[1].IsError, "cycle notification should be successful");
            }

            using var oversized = JsonDocument.Parse(
                await SendControlRequestAsync(new string('x', 4097)));
            TestAssert.False(
                oversized.RootElement.GetProperty("ok").GetBoolean(),
                "oversized request should be rejected");
            TestAssert.Equal(2, notifications.Count, "invalid request notification count");
            TestAssert.Equal(1, serverErrors.Count, "invalid request log count");
            TestAssert.Contains("exceeds 4096", serverErrors[0]);
        }
    }

    private static async Task<string> SendControlRequestAsync(string request)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            NamedPipeControlServer.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        await writer.WriteLineAsync(request);
        return await reader.ReadLineAsync()
            ?? throw new InvalidDataException("The local control server returned no response.");
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
