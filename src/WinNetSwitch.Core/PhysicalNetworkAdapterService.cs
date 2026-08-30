using System.Diagnostics;
using WinNetSwitch.Core.PowerShell;

namespace WinNetSwitch.Core;

/// <summary>
/// Lists physical adapters and independently enables or disables one selected adapter.
/// </summary>
public sealed class PhysicalNetworkAdapterService : INetworkAdapterService, IDisposable
{
    private readonly IPowerShellRunner _runner;
    private readonly IWirelessRadioController _wirelessRadioController;
    private readonly SemaphoreSlim _toggleLock = new(1, 1);
    private readonly object _snapshotLock = new();
    private readonly int _verificationAttempts;
    private readonly TimeSpan _verificationDelay;
    private AdapterSnapshot? _latestSnapshot;
    private long _observationSequence;
    private bool _disposed;

    public PhysicalNetworkAdapterService(
        IPowerShellRunner runner,
        IWirelessRadioController? wirelessRadioController = null,
        int verificationAttempts = 5,
        TimeSpan? verificationDelay = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _wirelessRadioController = wirelessRadioController ?? new NativeWirelessRadioController();
        _verificationAttempts = verificationAttempts > 0
            ? verificationAttempts
            : throw new ArgumentOutOfRangeException(
                nameof(verificationAttempts),
                "At least one verification attempt is required.");
        _verificationDelay = verificationDelay ?? TimeSpan.FromMilliseconds(250);
        if (_verificationDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verificationDelay),
                "The verification delay cannot be negative.");
        }
    }

    public async Task<IReadOnlyList<PhysicalNetworkAdapter>> GetPhysicalAdaptersAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var observationSequence = Interlocked.Increment(ref _observationSequence);
        var result = await _runner
            .RunAsync(NetAdapterScripts.ListPhysicalAdapters, cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSucceeded(result, "retrieve the physical network adapter list");

        var adapters = NetAdapterJsonParser.Parse(result.StandardOutput);
        var enrichedAdapters = new PhysicalNetworkAdapter[adapters.Count];
        for (var index = 0; index < adapters.Count; index++)
        {
            var adapter = adapters[index];
            var radioState = adapter.IsEnabled
                ? await _wirelessRadioController
                    .GetStateAsync(adapter.Id, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            enrichedAdapters[index] = adapter with { WirelessRadio = radioState };
        }

        PublishSnapshot(enrichedAdapters, observationSequence);
        return enrichedAdapters;
    }

    public async Task<IReadOnlyList<PhysicalNetworkAdapter>> SetAdapterEnabledAsync(
        Guid targetAdapterId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (targetAdapterId == Guid.Empty)
        {
            throw new ArgumentException("Adapter ID cannot be empty.", nameof(targetAdapterId));
        }

        await _toggleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var initialTarget = GetRecentSnapshot()
                ?.SingleOrDefault(adapter => adapter.Id == targetAdapterId);
            return await SetAdapterEnabledCoreAsync(
                    targetAdapterId,
                    enabled,
                    cancellationToken,
                    initialTarget)
                .ConfigureAwait(false);
        }
        finally
        {
            _toggleLock.Release();
        }
    }

    public async Task<IReadOnlyList<PhysicalNetworkAdapter>> EnableOnlyAsync(
        Guid targetAdapterId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (targetAdapterId == Guid.Empty)
        {
            throw new ArgumentException("Adapter ID cannot be empty.", nameof(targetAdapterId));
        }

        await _toggleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PhysicalNetworkAdapter>? initialState = null;
        try
        {
            initialState = GetRecentSnapshot() ??
                await GetPhysicalAdaptersAsync(cancellationToken).ConfigureAwait(false);
            var initialTarget = initialState.SingleOrDefault(adapter => adapter.Id == targetAdapterId)
                ?? throw new NetworkSwitchException(
                    $"Physical network adapter {targetAdapterId:D} is no longer available. Refresh the list.");

            var enabledState = await SetAdapterEnabledCoreAsync(
                    initialTarget.Id,
                    enabled: true,
                    cancellationToken,
                    initialTarget)
                .ConfigureAwait(false);
            var enabledTarget = enabledState.Single(
                adapter => IsSameDevice(adapter, initialTarget));

            var adaptersToDisable = enabledState
                .Where(adapter => !IsSameDevice(adapter, enabledTarget) && adapter.IsEnabled)
                .Select(adapter => adapter.Id)
                .ToArray();
            var currentState = enabledState;
            foreach (var adapterId in adaptersToDisable)
            {
                var currentAdapter = currentState.Single(adapter => adapter.Id == adapterId);
                currentState = await SetAdapterEnabledCoreAsync(
                        currentAdapter.Id,
                        enabled: false,
                        cancellationToken,
                        currentAdapter)
                    .ConfigureAwait(false);
            }

            var finalState = currentState;
            return finalState.Count(adapter => adapter.IsEnabled) == 1 &&
                   finalState.Any(adapter => IsSameDevice(adapter, enabledTarget) && adapter.IsActive)
                ? finalState
                : throw new NetworkSwitchException(
                    $"Windows did not confirm that “{initialTarget.Name}” is the only enabled adapter.");
        }
        catch (Exception exception) when (initialState is not null)
        {
            var rollbackErrors = new List<string>();
            foreach (var adapter in initialState)
            {
                var rollbackError = await RestoreTargetStateAsync(adapter).ConfigureAwait(false);
                if (rollbackError is not null)
                {
                    rollbackErrors.Add($"{adapter.Name}: {rollbackError}");
                }
            }

            var rollbackStatus = rollbackErrors.Count == 0
                ? "The original adapter states were restored."
                : $"Restoration completed with errors: {string.Join("; ", rollbackErrors)}";
            throw new NetworkSwitchException(
                $"Could not enable only the selected adapter. {rollbackStatus} Cause: {exception.Message}",
                exception);
        }
        finally
        {
            _toggleLock.Release();
        }
    }

    private async Task<IReadOnlyList<PhysicalNetworkAdapter>> SetAdapterEnabledCoreAsync(
        Guid targetAdapterId,
        bool enabled,
        CancellationToken cancellationToken,
        PhysicalNetworkAdapter? knownInitialTarget = null)
    {
        PhysicalNetworkAdapter? initialTarget = null;
        IReadOnlyList<PhysicalNetworkAdapter>? stateAfterAdapterEnable = null;
        var adapterStateChanged = false;
        var radioStateChanged = false;
        try
        {
            initialTarget = knownInitialTarget;
            if (initialTarget is null)
            {
                var initialState = await GetPhysicalAdaptersAsync(cancellationToken).ConfigureAwait(false);
                initialTarget = initialState.SingleOrDefault(adapter => adapter.Id == targetAdapterId)
                    ?? throw new NetworkSwitchException(
                        $"Physical network adapter {targetAdapterId:D} is no longer available. Refresh the list.");
            }

            if (enabled)
            {
                var targetAfterAdapterEnable = initialTarget;
                if (!initialTarget.IsEnabled)
                {
                    var enableScript = string.IsNullOrWhiteSpace(initialTarget.DeviceInstanceId)
                        ? NetAdapterScripts.Enable(initialTarget.Id)
                        : NetAdapterScripts.EnablePnpDevice(initialTarget.DeviceInstanceId);
                    await RunMutationAsync(
                            enableScript,
                            $"enable adapter “{initialTarget.Name}”",
                            cancellationToken)
                        .ConfigureAwait(false);
                    adapterStateChanged = true;

                    stateAfterAdapterEnable = await WaitForStateAsync(
                        adapters => adapters.Any(
                                adapter => IsSameDevice(adapter, initialTarget) && adapter.IsEnabled),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (stateAfterAdapterEnable is null)
                    {
                        throw new NetworkSwitchException(
                            $"Windows did not confirm that adapter “{initialTarget.Name}” was enabled.");
                    }

                    targetAfterAdapterEnable = stateAfterAdapterEnable.Single(
                        adapter => IsSameDevice(adapter, initialTarget));
                }

                var isWireless = targetAfterAdapterEnable.WirelessRadio is not null;
                if (isWireless)
                {
                    radioStateChanged = initialTarget.WirelessRadio?.SoftwareOn != true;
                    _ = await _wirelessRadioController
                        .SetSoftwareStateAsync(
                            targetAfterAdapterEnable.Id,
                            enabled: true,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (stateAfterAdapterEnable is not null)
                    {
                        var verifiedRadioState = await WaitForWirelessRadioStateAsync(
                                targetAfterAdapterEnable.Id,
                                enabled: true,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (verifiedRadioState?.IsOn != true)
                        {
                            throw new NetworkSwitchException(
                                $"Windows did not confirm that the Wi-Fi radio for “{initialTarget.Name}” was enabled. " +
                                "Check airplane mode and the hardware wireless switch.");
                        }

                        var verifiedState = stateAfterAdapterEnable
                            .Select(adapter => IsSameDevice(adapter, initialTarget)
                                ? adapter with { WirelessRadio = verifiedRadioState }
                                : adapter)
                            .ToArray();
                        PublishSnapshot(
                            verifiedState,
                            Interlocked.Increment(ref _observationSequence));
                        return verifiedState;
                    }
                }

                var finalState = await WaitForStateAsync(
                        adapters => adapters.Any(
                            adapter => IsSameDevice(adapter, initialTarget) && adapter.IsActive),
                        cancellationToken)
                    .ConfigureAwait(false);
                return finalState ?? throw new NetworkSwitchException(
                    isWireless
                        ? $"Windows did not confirm that the Wi-Fi radio for “{initialTarget.Name}” was enabled. " +
                          "Check airplane mode and the hardware wireless switch."
                        : $"Windows did not confirm that adapter “{initialTarget.Name}” was enabled.");
            }

            if (initialTarget.WirelessRadio is not null && initialTarget.WirelessRadio.SoftwareOn)
            {
                radioStateChanged = true;
                _ = await _wirelessRadioController
                    .SetSoftwareStateAsync(initialTarget.Id, enabled: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (initialTarget.IsEnabled)
            {
                await RunMutationAsync(
                        NetAdapterScripts.Disable(initialTarget.Id),
                        $"disable adapter “{initialTarget.Name}”",
                        cancellationToken)
                    .ConfigureAwait(false);
                adapterStateChanged = true;
            }

            var disabledState = await WaitForStateAsync(
                    adapters => adapters.Any(
                        adapter => IsSameDevice(adapter, initialTarget) && !adapter.IsEnabled),
                    cancellationToken)
                .ConfigureAwait(false);
            return disabledState ?? throw new NetworkSwitchException(
                $"Windows did not confirm that adapter “{initialTarget.Name}” was disabled.");
        }
        catch (Exception exception) when (
            initialTarget is not null && (adapterStateChanged || radioStateChanged))
        {
            var rollbackError = await RestoreTargetStateAsync(initialTarget).ConfigureAwait(false);
            var rollbackStatus = rollbackError is null
                ? "The original adapter state was restored."
                : $"The original state could not be fully restored: {rollbackError}";

            if (exception is OperationCanceledException && rollbackError is null)
            {
                throw;
            }

            throw new NetworkSwitchException(
                $"The network state change did not complete. {rollbackStatus} Cause: {exception.Message}",
                exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _toggleLock.Dispose();
    }

    private async Task<IReadOnlyList<PhysicalNetworkAdapter>?> WaitForStateAsync(
        Func<IReadOnlyList<PhysicalNetworkAdapter>, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _verificationAttempts; attempt++)
        {
            var adapters = await GetPhysicalAdaptersAsync(cancellationToken).ConfigureAwait(false);
            if (predicate(adapters))
            {
                return adapters;
            }

            if (attempt + 1 < _verificationAttempts && _verificationDelay > TimeSpan.Zero)
            {
                await Task.Delay(_verificationDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task<WirelessRadioState?> WaitForWirelessRadioStateAsync(
        Guid interfaceId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        WirelessRadioState? lastState = null;
        for (var attempt = 0; attempt < _verificationAttempts; attempt++)
        {
            lastState = await _wirelessRadioController
                .GetStateAsync(interfaceId, cancellationToken)
                .ConfigureAwait(false);
            if (lastState is not null && lastState.IsOn == enabled)
            {
                return lastState;
            }

            if (attempt + 1 < _verificationAttempts && _verificationDelay > TimeSpan.Zero)
            {
                await Task.Delay(_verificationDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return lastState;
    }

    private async Task RunMutationAsync(
        string script,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(script, cancellationToken).ConfigureAwait(false);
        EnsureCommandSucceeded(result, operation);
    }

    private async Task<string?> RestoreTargetStateAsync(PhysicalNetworkAdapter initialTarget)
    {
        try
        {
            var currentState = await GetPhysicalAdaptersAsync(CancellationToken.None).ConfigureAwait(false);
            var currentTarget = currentState.SingleOrDefault(
                    adapter => IsSameDevice(adapter, initialTarget))
                ?? throw new NetworkSwitchException(
                    $"Adapter “{initialTarget.Name}” disappeared during restoration.");

            if (initialTarget.IsEnabled && !currentTarget.IsEnabled)
            {
                await RunMutationAsync(
                        string.IsNullOrWhiteSpace(initialTarget.DeviceInstanceId)
                            ? NetAdapterScripts.Enable(initialTarget.Id)
                            : NetAdapterScripts.EnablePnpDevice(initialTarget.DeviceInstanceId),
                        $"restore adapter “{initialTarget.Name}”",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (initialTarget.WirelessRadio is not null)
            {
                _ = await _wirelessRadioController
                    .SetSoftwareStateAsync(
                        initialTarget.Id,
                        initialTarget.WirelessRadio.SoftwareOn,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            currentState = await GetPhysicalAdaptersAsync(CancellationToken.None).ConfigureAwait(false);
            currentTarget = currentState.Single(adapter => IsSameDevice(adapter, initialTarget));
            if (!initialTarget.IsEnabled && currentTarget.IsEnabled)
            {
                await RunMutationAsync(
                        NetAdapterScripts.Disable(currentTarget.Id),
                        $"restore the disabled state of adapter “{initialTarget.Name}”",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var restoredState = await GetPhysicalAdaptersAsync(CancellationToken.None).ConfigureAwait(false);
            var restoredTarget = restoredState.Single(adapter => IsSameDevice(adapter, initialTarget));
            var radioRestored = initialTarget.WirelessRadio is null ||
                restoredTarget.WirelessRadio?.SoftwareOn == initialTarget.WirelessRadio.SoftwareOn;
            return restoredTarget.IsEnabled == initialTarget.IsEnabled && radioRestored
                ? null
                : "Windows returned a state that differs from the original state.";
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static void EnsureCommandSucceeded(PowerShellResult result, string operation)
    {
        if (result.IsSuccess)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"exit code {result.ExitCode}"
            : result.StandardError.Trim();
        throw new NetworkSwitchException(
            $"Windows PowerShell could not {operation}: {details}");
    }

    private static bool IsSameDevice(
        PhysicalNetworkAdapter candidate,
        PhysicalNetworkAdapter reference) =>
        !string.IsNullOrWhiteSpace(reference.DeviceInstanceId)
            ? string.Equals(
                candidate.DeviceInstanceId,
                reference.DeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)
            : candidate.Id == reference.Id;

    private IReadOnlyList<PhysicalNetworkAdapter>? GetRecentSnapshot()
    {
        lock (_snapshotLock)
        {
            return _latestSnapshot is not null &&
                   Stopwatch.GetElapsedTime(_latestSnapshot.Timestamp) <= TimeSpan.FromSeconds(10)
                ? _latestSnapshot.Adapters
                : null;
        }
    }

    private void PublishSnapshot(
        IReadOnlyList<PhysicalNetworkAdapter> adapters,
        long observationSequence)
    {
        lock (_snapshotLock)
        {
            if (_latestSnapshot is null || observationSequence >= _latestSnapshot.Sequence)
            {
                _latestSnapshot = new AdapterSnapshot(
                    adapters.ToArray(),
                    observationSequence,
                    Stopwatch.GetTimestamp());
            }
        }
    }

    private sealed record AdapterSnapshot(
        IReadOnlyList<PhysicalNetworkAdapter> Adapters,
        long Sequence,
        long Timestamp);
}
