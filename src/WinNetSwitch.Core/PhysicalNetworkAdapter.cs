namespace WinNetSwitch.Core;

/// <summary>
/// A physical network adapter reported by the Windows NetAdapter module.
/// </summary>
public sealed record PhysicalNetworkAdapter(
    Guid Id,
    string? DeviceInstanceId,
    int InterfaceIndex,
    string Name,
    string Description,
    string Status,
    string MediaConnectionState,
    string LinkSpeed,
    bool IsEnabled,
    WirelessRadioState? WirelessRadio)
{
    public bool IsWireless => WirelessRadio is not null;

    public bool IsActive => IsEnabled && (WirelessRadio?.IsOn ?? true);
}
