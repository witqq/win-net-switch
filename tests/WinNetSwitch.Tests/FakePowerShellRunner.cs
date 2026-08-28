using System.Text.Json;
using WinNetSwitch.Core;
using WinNetSwitch.Core.PowerShell;

namespace WinNetSwitch.Tests;

internal sealed class FakePowerShellRunner : IPowerShellRunner
{
    private int _activeCalls;
    private int _maximumConcurrentCalls;

    internal FakePowerShellRunner(params PhysicalNetworkAdapter[] adapters)
        : this(operationLog: null, adapters)
    {
    }

    internal FakePowerShellRunner(
        List<string>? operationLog,
        params PhysicalNetworkAdapter[] adapters)
    {
        Adapters = adapters.ToDictionary(adapter => adapter.Id);
        OperationLog = operationLog;
    }

    internal Dictionary<Guid, PhysicalNetworkAdapter> Adapters { get; }

    private List<string>? OperationLog { get; }

    internal List<string> Scripts { get; } = [];

    internal TaskCompletionSource FirstCallStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Guid? FailEnableFor { get; set; }

    internal Guid? FailDisableFor { get; set; }

    internal Guid? IgnoreEnableFor { get; set; }

    internal Guid? IgnoreDisableFor { get; set; }

    internal string? ListOutputOverride { get; set; }

    internal TimeSpan RunDelay { get; set; }

    internal int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

    public async Task<PowerShellResult> RunAsync(
        string script,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Scripts.Add(script);
        FirstCallStarted.TrySetResult();
        var activeCalls = Interlocked.Increment(ref _activeCalls);
        UpdateMaximumConcurrentCalls(activeCalls);

        try
        {
            if (RunDelay > TimeSpan.Zero)
            {
                await Task.Delay(RunDelay, cancellationToken);
            }

            if (script == NetAdapterScripts.ListPhysicalAdapters)
            {
                OperationLog?.Add("powershell:list");
                if (ListOutputOverride is not null)
                {
                    return new PowerShellResult(0, ListOutputOverride, string.Empty);
                }

                var json = JsonSerializer.Serialize(
                    Adapters.Values.OrderBy(adapter => adapter.Name).Select(adapter => new
                    {
                        Id = adapter.Id.ToString("D"),
                        adapter.DeviceInstanceId,
                        adapter.InterfaceIndex,
                        adapter.Name,
                        adapter.Description,
                        adapter.Status,
                        adapter.MediaConnectionState,
                        adapter.LinkSpeed,
                        adapter.IsEnabled,
                    }));
                return new PowerShellResult(0, json, string.Empty);
            }

            foreach (var adapter in Adapters.Values.ToArray())
            {
                if (script == NetAdapterScripts.Enable(adapter.Id) ||
                    (!string.IsNullOrWhiteSpace(adapter.DeviceInstanceId) &&
                     script == NetAdapterScripts.EnablePnpDevice(adapter.DeviceInstanceId)))
                {
                    OperationLog?.Add($"powershell:enable:{adapter.Id:D}");
                    if (FailEnableFor == adapter.Id)
                    {
                        return new PowerShellResult(1, string.Empty, "enable failed");
                    }

                    if (IgnoreEnableFor == adapter.Id)
                    {
                        return new PowerShellResult(0, string.Empty, string.Empty);
                    }

                    Adapters[adapter.Id] = adapter with
                    {
                        IsEnabled = true,
                        Status = "Up",
                        MediaConnectionState = "Connected",
                    };
                    return new PowerShellResult(0, string.Empty, string.Empty);
                }

                if (script == NetAdapterScripts.Disable(adapter.Id))
                {
                    OperationLog?.Add($"powershell:disable:{adapter.Id:D}");
                    if (FailDisableFor == adapter.Id)
                    {
                        return new PowerShellResult(1, string.Empty, "disable failed");
                    }

                    if (IgnoreDisableFor == adapter.Id)
                    {
                        return new PowerShellResult(0, string.Empty, string.Empty);
                    }

                    Adapters[adapter.Id] = adapter with
                    {
                        IsEnabled = false,
                        Status = "Disabled",
                        MediaConnectionState = "Unknown",
                    };
                    return new PowerShellResult(0, string.Empty, string.Empty);
                }
            }

            return new PowerShellResult(1, string.Empty, "unexpected script");
        }
        finally
        {
            _ = Interlocked.Decrement(ref _activeCalls);
        }
    }

    private void UpdateMaximumConcurrentCalls(int activeCalls)
    {
        while (true)
        {
            var currentMaximum = Volatile.Read(ref _maximumConcurrentCalls);
            if (activeCalls <= currentMaximum ||
                Interlocked.CompareExchange(
                    ref _maximumConcurrentCalls,
                    activeCalls,
                    currentMaximum) == currentMaximum)
            {
                return;
            }
        }
    }
}
