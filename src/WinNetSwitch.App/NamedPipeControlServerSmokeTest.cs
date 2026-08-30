using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using WinNetSwitch.Core;

namespace WinNetSwitch.App;

internal static class NamedPipeControlServerSmokeTest
{
    private static readonly Guid WifiId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EthernetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal static async Task<int> RunAsync()
    {
        var service = new SmokeNetworkAdapterService();
        using var server = new NamedPipeControlServer(service);
        server.Start();

        var list = await SendAsync("""{"version":1,"command":"list"}""");
        if (!IsSuccessfulResponse(list, expectedAdapterCount: 2))
        {
            return 1;
        }

        var toggle = await SendAsync(
            $$"""{"version":1,"command":"toggle","adapterId":"{{WifiId:D}}"}""");
        if (!IsSuccessfulResponse(toggle, expectedAdapterCount: 2) ||
            service.ToggleCalls != 1 ||
            service.LastToggledAdapterId != WifiId)
        {
            return 2;
        }

        var cycle = await SendAsync("""{"version":1,"command":"cycle"}""");
        if (!IsSuccessfulResponse(cycle, expectedAdapterCount: 2) || service.CycleCalls != 1)
        {
            return 3;
        }

        var invalid = await SendAsync("""{"version":1,"command":"toggle"}""");
        using var invalidJson = JsonDocument.Parse(invalid);
        if (invalidJson.RootElement.GetProperty("ok").GetBoolean())
        {
            return 4;
        }

        var oversized = await SendAsync(new string('x', 4097));
        using var oversizedJson = JsonDocument.Parse(oversized);
        return oversizedJson.RootElement.GetProperty("ok").GetBoolean() ? 5 : 0;
    }

    private static async Task<string> SendAsync(string request)
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

    private static bool IsSuccessfulResponse(string response, int expectedAdapterCount)
    {
        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        return root.GetProperty("version").GetInt32() == 1 &&
               root.GetProperty("ok").GetBoolean() &&
               root.GetProperty("adapters").GetArrayLength() == expectedAdapterCount;
    }

    private sealed class SmokeNetworkAdapterService : INetworkAdapterService
    {
        private static readonly IReadOnlyList<PhysicalNetworkAdapter> Adapters =
        [
            new(
                WifiId,
                "PCI\\WIFI",
                7,
                "Wi-Fi",
                "Smoke wireless adapter",
                "Up",
                "Connected",
                "866.7 Mbps",
                IsEnabled: true,
                WirelessRadio: new WirelessRadioState(true, true, 1)),
            new(
                EthernetId,
                "PCI\\ETHERNET",
                12,
                "Ethernet",
                "Smoke wired adapter",
                "Disabled",
                "Unknown",
                "1 Gbps",
                IsEnabled: false,
                WirelessRadio: null),
        ];

        internal int ToggleCalls { get; private set; }

        internal int CycleCalls { get; private set; }

        internal Guid? LastToggledAdapterId { get; private set; }

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> GetPhysicalAdaptersAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Adapters);

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> SetAdapterEnabledAsync(
            Guid targetAdapterId,
            bool enabled,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The IPC smoke test must use the toggle command.");

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> ToggleAdapterAsync(
            Guid targetAdapterId,
            CancellationToken cancellationToken = default)
        {
            ToggleCalls++;
            LastToggledAdapterId = targetAdapterId;
            return Task.FromResult(Adapters);
        }

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> EnableOnlyAsync(
            Guid targetAdapterId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The IPC smoke test must use the cycle command.");

        public Task<IReadOnlyList<PhysicalNetworkAdapter>> CycleToNextAsync(
            CancellationToken cancellationToken = default)
        {
            CycleCalls++;
            return Task.FromResult(Adapters);
        }
    }
}
