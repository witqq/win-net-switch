namespace WinNetSwitch.Core;

public sealed class NetworkSwitchException : Exception
{
    public NetworkSwitchException(string message)
        : base(message)
    {
    }

    public NetworkSwitchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
