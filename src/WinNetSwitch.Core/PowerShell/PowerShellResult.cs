namespace WinNetSwitch.Core.PowerShell;

public sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
