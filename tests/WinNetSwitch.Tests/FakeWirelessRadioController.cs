using WinNetSwitch.Core;

namespace WinNetSwitch.Tests;

internal sealed class FakeWirelessRadioController : IWirelessRadioController
{
    private readonly List<string>? _operationLog;

    internal FakeWirelessRadioController(
        List<string>? operationLog = null,
        params (Guid Id, WirelessRadioState State)[] interfaces)
    {
        _operationLog = operationLog;
        States = interfaces.ToDictionary(item => item.Id, item => item.State);
    }

    internal Dictionary<Guid, WirelessRadioState> States { get; }

    internal List<(Guid Id, bool Enabled)> SetCalls { get; } = [];

    internal Guid? FailSetFor { get; set; }

    internal bool MutateBeforeFailure { get; set; }

    internal bool FailOnlyOnce { get; set; }

    public Task<WirelessRadioState?> GetStateAsync(
        Guid interfaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operationLog?.Add($"radio:get:{interfaceId:D}");
        return Task.FromResult(
            States.TryGetValue(interfaceId, out var state) ? state : null);
    }

    public Task<bool> SetSoftwareStateAsync(
        Guid interfaceId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operationLog?.Add($"radio:set:{interfaceId:D}:{enabled}");
        if (!States.TryGetValue(interfaceId, out var state))
        {
            return Task.FromResult(false);
        }

        SetCalls.Add((interfaceId, enabled));
        if (FailSetFor == interfaceId)
        {
            if (MutateBeforeFailure)
            {
                States[interfaceId] = state with { SoftwareOn = enabled };
            }

            if (FailOnlyOnce)
            {
                FailSetFor = null;
            }

            throw new NetworkSwitchException("radio change failed");
        }

        States[interfaceId] = state with { SoftwareOn = enabled };
        return Task.FromResult(true);
    }
}
