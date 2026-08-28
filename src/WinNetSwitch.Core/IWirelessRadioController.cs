namespace WinNetSwitch.Core;

public interface IWirelessRadioController
{
    Task<WirelessRadioState?> GetStateAsync(
        Guid interfaceId,
        CancellationToken cancellationToken = default);

    Task<bool> SetSoftwareStateAsync(
        Guid interfaceId,
        bool enabled,
        CancellationToken cancellationToken = default);
}
