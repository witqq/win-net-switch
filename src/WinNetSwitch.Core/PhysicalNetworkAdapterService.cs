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
    private readonly int _verificationAttempts;
    private readonly TimeSpan _verificationDelay;
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
        var result = await _runner
            .RunAsync(NetAdapterScripts.ListPhysicalAdapters, cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSucceeded(result, "получить список физических сетевых адаптеров");

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
        PhysicalNetworkAdapter? initialTarget = null;
        var adapterStateChanged = false;
        var radioStateChanged = false;
        try
        {
            var initialState = await GetPhysicalAdaptersAsync(cancellationToken).ConfigureAwait(false);
            initialTarget = initialState.SingleOrDefault(adapter => adapter.Id == targetAdapterId)
                ?? throw new NetworkSwitchException(
                    $"Физический сетевой адаптер {targetAdapterId:D} больше не найден. Обновите список.");

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
                            $"включить адаптер «{initialTarget.Name}»",
                            cancellationToken)
                        .ConfigureAwait(false);
                    adapterStateChanged = true;

                    var enabledState = await WaitForStateAsync(
                        adapters => adapters.Any(
                                adapter => IsSameDevice(adapter, initialTarget) && adapter.IsEnabled),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (enabledState is null)
                    {
                        throw new NetworkSwitchException(
                            $"Windows не подтвердила включение адаптера «{initialTarget.Name}».");
                    }

                    targetAfterAdapterEnable = enabledState.Single(
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
                }

                var finalState = await WaitForStateAsync(
                        adapters => adapters.Any(
                            adapter => IsSameDevice(adapter, initialTarget) && adapter.IsActive),
                        cancellationToken)
                    .ConfigureAwait(false);
                return finalState ?? throw new NetworkSwitchException(
                    isWireless
                        ? $"Windows не подтвердила включение Wi-Fi radio для «{initialTarget.Name}». " +
                          "Проверьте режим полёта или аппаратный переключатель беспроводной связи."
                        : $"Windows не подтвердила включение адаптера «{initialTarget.Name}».");
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
                        $"отключить адаптер «{initialTarget.Name}»",
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
                $"Windows не подтвердила отключение адаптера «{initialTarget.Name}».");
        }
        catch (Exception exception) when (
            initialTarget is not null && (adapterStateChanged || radioStateChanged))
        {
            var rollbackError = await RestoreTargetStateAsync(initialTarget).ConfigureAwait(false);
            var rollbackStatus = rollbackError is null
                ? "Исходное состояние адаптера восстановлено."
                : $"Не удалось полностью восстановить исходное состояние: {rollbackError}";

            if (exception is OperationCanceledException && rollbackError is null)
            {
                throw;
            }

            throw new NetworkSwitchException(
                $"Изменение состояния сети не завершено. {rollbackStatus} Причина: {exception.Message}",
                exception);
        }
        finally
        {
            _toggleLock.Release();
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
                    $"Адаптер «{initialTarget.Name}» исчез во время восстановления.");

            if (initialTarget.IsEnabled && !currentTarget.IsEnabled)
            {
                await RunMutationAsync(
                        string.IsNullOrWhiteSpace(initialTarget.DeviceInstanceId)
                            ? NetAdapterScripts.Enable(initialTarget.Id)
                            : NetAdapterScripts.EnablePnpDevice(initialTarget.DeviceInstanceId),
                        $"восстановить адаптер «{initialTarget.Name}»",
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
                        $"восстановить отключённое состояние адаптера «{initialTarget.Name}»",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var restoredState = await GetPhysicalAdaptersAsync(CancellationToken.None).ConfigureAwait(false);
            var restoredTarget = restoredState.Single(adapter => IsSameDevice(adapter, initialTarget));
            var radioRestored = initialTarget.WirelessRadio is null ||
                restoredTarget.WirelessRadio?.SoftwareOn == initialTarget.WirelessRadio.SoftwareOn;
            return restoredTarget.IsEnabled == initialTarget.IsEnabled && radioRestored
                ? null
                : "Windows вернула состояние, отличающееся от исходного.";
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
            ? $"код выхода {result.ExitCode}"
            : result.StandardError.Trim();
        throw new NetworkSwitchException(
            $"Windows PowerShell не смог {operation}: {details}");
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
}
