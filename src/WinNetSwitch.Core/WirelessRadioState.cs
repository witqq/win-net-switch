namespace WinNetSwitch.Core;

public sealed record WirelessRadioState(
    bool SoftwareOn,
    bool HardwareOn,
    int PhysicalLayerCount)
{
    public bool IsOn => SoftwareOn && HardwareOn;
}
