namespace WinNetSwitch.Core;

/// <summary>
/// A physical network adapter reported by the Windows NetAdapter module.
/// </summary>
public sealed record PhysicalNetworkAdapter(
    Guid Id,
    int InterfaceIndex,
    string Name,
    string Description,
    string Status,
    string MediaConnectionState,
    string LinkSpeed,
    bool IsEnabled);
