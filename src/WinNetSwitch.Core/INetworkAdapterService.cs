namespace WinNetSwitch.Core;

public interface INetworkAdapterService
{
    Task<IReadOnlyList<PhysicalNetworkAdapter>> GetPhysicalAdaptersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhysicalNetworkAdapter>> SetAdapterEnabledAsync(
        Guid targetAdapterId,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhysicalNetworkAdapter>> EnableOnlyAsync(
        Guid targetAdapterId,
        CancellationToken cancellationToken = default);
}
