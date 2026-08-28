namespace WinNetSwitch.Core;

public interface INetworkAdapterService
{
    Task<IReadOnlyList<PhysicalNetworkAdapter>> GetPhysicalAdaptersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhysicalNetworkAdapter>> SwitchExclusivelyAsync(
        Guid targetAdapterId,
        CancellationToken cancellationToken = default);
}
