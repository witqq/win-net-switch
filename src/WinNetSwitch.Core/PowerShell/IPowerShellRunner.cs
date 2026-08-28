namespace WinNetSwitch.Core.PowerShell;

public interface IPowerShellRunner
{
    Task<PowerShellResult> RunAsync(string script, CancellationToken cancellationToken = default);
}
