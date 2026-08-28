using WinNetSwitch.Core.PowerShell;

namespace WinNetSwitch.Core;

/// <summary>
/// Lists physical adapters and switches their enabled state while preserving the initial state on failure.
/// </summary>
public sealed class PhysicalNetworkAdapterService : INetworkAdapterService, IDisposable
{
    private readonly IPowerShellRunner _runner;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly int _verificationAttempts;
    private readonly TimeSpan _verificationDelay;
    private bool _disposed;

    public PhysicalNetworkAdapterService(
        IPowerShellRunner runner,
        int verificationAttempts = 5,
        TimeSpan? verificationDelay = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
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
        return NetAdapterJsonParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<PhysicalNetworkAdapter>> SwitchExclusivelyAsync(
        Guid targetAdapterId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (targetAdapterId == Guid.Empty)
        {
            throw new ArgumentException("Adapter ID cannot be empty.", nameof(targetAdapterId));
        }

        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PhysicalNetworkAdapter>? initialState = null;
        var mutationAttempted = false;
        try
        {
            initialState = await GetPhysicalAdaptersAsync(cancellationToken).ConfigureAwait(false);
            var target = initialState.SingleOrDefault(adapter => adapter.Id == targetAdapterId)
                ?? throw new NetworkSwitchException(
                    $"Физический сетевой адаптер {targetAdapterId:D} больше не найден. Обновите список.");

            if (!target.IsEnabled)
            {
                mutationAttempted = true;
                await RunMutationAsync(
                        NetAdapterScripts.Enable(target.Id),
                        $"включить адаптер «{target.Name}»",
                        cancellationToken)
                    .ConfigureAwait(false);

                var enabledState = await WaitForStateAsync(
                        adapters => adapters.Any(adapter => adapter.Id == target.Id && adapter.IsEnabled),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (enabledState is null)
                {
                    throw new NetworkSwitchException(
                        $"Windows не подтвердила включение адаптера «{target.Name}». Другие адаптеры не будут отключены.");
                }
            }

            var currentState = await GetPhysicalAdaptersAsync(cancellationToken).ConfigureAwait(false);
            foreach (var adapter in currentState.Where(
                         adapter => adapter.Id != target.Id && adapter.IsEnabled))
            {
                mutationAttempted = true;
                await RunMutationAsync(
                        NetAdapterScripts.Disable(adapter.Id),
                        $"отключить адаптер «{adapter.Name}»",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var finalState = await WaitForStateAsync(
                    adapters =>
                        adapters.Count(adapter => adapter.IsEnabled) == 1 &&
                        adapters.Any(adapter => adapter.Id == target.Id && adapter.IsEnabled),
                    cancellationToken)
                .ConfigureAwait(false);

            return finalState ?? throw new NetworkSwitchException(
                $"Windows не подтвердила, что «{target.Name}» остался единственным включённым физическим адаптером.");
        }
        catch (Exception exception) when (mutationAttempted && initialState is not null)
        {
            var rollbackError = await RestoreStateAsync(initialState).ConfigureAwait(false);
            var rollbackStatus = rollbackError is null
                ? "Исходное состояние адаптеров восстановлено."
                : $"Не удалось полностью восстановить исходное состояние: {rollbackError}";

            if (exception is OperationCanceledException && rollbackError is null)
            {
                throw;
            }

            throw new NetworkSwitchException(
                $"Переключение сети не завершено. {rollbackStatus} Причина: {exception.Message}",
                exception);
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _switchLock.Dispose();
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

    private async Task<string?> RestoreStateAsync(
        IReadOnlyList<PhysicalNetworkAdapter> initialState)
    {
        try
        {
            var currentState = await GetPhysicalAdaptersAsync(CancellationToken.None).ConfigureAwait(false);
            var currentById = currentState.ToDictionary(adapter => adapter.Id);

            foreach (var adapter in initialState.Where(adapter => adapter.IsEnabled))
            {
                if (currentById.TryGetValue(adapter.Id, out var current) && !current.IsEnabled)
                {
                    await RunMutationAsync(
                            NetAdapterScripts.Enable(adapter.Id),
                            $"восстановить адаптер «{adapter.Name}»",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            currentState = await GetPhysicalAdaptersAsync(CancellationToken.None).ConfigureAwait(false);
            var initiallyEnabledIds = initialState
                .Where(adapter => adapter.IsEnabled)
                .Select(adapter => adapter.Id)
                .ToHashSet();
            foreach (var adapter in currentState.Where(
                         adapter => adapter.IsEnabled && !initiallyEnabledIds.Contains(adapter.Id)))
            {
                await RunMutationAsync(
                        NetAdapterScripts.Disable(adapter.Id),
                        $"восстановить отключённое состояние адаптера «{adapter.Name}»",
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var restoredState = await GetPhysicalAdaptersAsync(CancellationToken.None).ConfigureAwait(false);
            var restoredEnabledIds = restoredState
                .Where(adapter => adapter.IsEnabled)
                .Select(adapter => adapter.Id)
                .ToHashSet();
            return restoredEnabledIds.SetEquals(initiallyEnabledIds)
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
}
