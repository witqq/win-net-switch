using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace WinNetSwitch.Windows;

public static class InstallationManager
{
    private const string UninstallRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinNetSwitch";
    private const int MoveFileDelayUntilReboot = 0x4;

    public static void Install(string payloadPath, bool startAfterInstall)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPath);
        if (!File.Exists(payloadPath))
        {
            throw new FileNotFoundException("The WinNetSwitch payload was not found.", payloadPath);
        }

        StopInstalledProcesses();
        Directory.CreateDirectory(InstallationPaths.InstallDirectory);
        var temporaryPath = string.Concat(InstallationPaths.ApplicationPath, ".new");
        try
        {
            File.Copy(payloadPath, temporaryPath, overwrite: true);
            File.Move(temporaryPath, InstallationPaths.ApplicationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        CreateStartMenuShortcut();
        CreateAutostartTask();
        RegisterUninstaller();

        if (startAfterInstall)
        {
            RunScheduledTask(
                "/Run",
                "/TN",
                InstallationPaths.ScheduledTaskName);
        }
    }

    public static void BeginUninstall(bool silent)
    {
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        var workerPath = Path.Combine(
            Path.GetTempPath(),
            $"WinNetSwitch-Uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(currentExecutable, workerPath, overwrite: false);

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add("--uninstall-worker");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        if (silent)
        {
            startInfo.ArgumentList.Add("--silent");
        }

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The uninstall worker could not be started.");
    }

    public static async Task RunUninstallWorkerAsync(
        int parentProcessId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }

        DeleteAutostartTask();
        StopInstalledProcesses();
        if (File.Exists(InstallationPaths.ShortcutPath))
        {
            File.Delete(InstallationPaths.ShortcutPath);
        }

        Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
        if (Directory.Exists(InstallationPaths.InstallDirectory))
        {
            Directory.Delete(InstallationPaths.InstallDirectory, recursive: true);
        }

        if (Directory.Exists(InstallationPaths.DataDirectory))
        {
            Directory.Delete(InstallationPaths.DataDirectory, recursive: true);
        }

        var workerPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(workerPath))
        {
            _ = MoveFileEx(workerPath, null, MoveFileDelayUntilReboot);
        }
    }

    public static bool IsInstalled() =>
        File.Exists(InstallationPaths.ApplicationPath) &&
        File.Exists(InstallationPaths.ShortcutPath) &&
        HasUninstallRegistryKey();

    private static void CreateAutostartTask()
    {
        var taskXmlPath = Path.Combine(
            Path.GetTempPath(),
            $"WinNetSwitch-Task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(
                taskXmlPath,
                CreateAutostartTaskXml(),
                Encoding.Unicode);
            RunScheduledTask(
                "/Create",
                "/TN",
                InstallationPaths.ScheduledTaskName,
                "/XML",
                taskXmlPath,
                "/F");
        }
        finally
        {
            if (File.Exists(taskXmlPath))
            {
                File.Delete(taskXmlPath);
            }
        }
    }

    private static void DeleteAutostartTask()
    {
        _ = RunScheduledTask(allowFailure: true, "/Delete", "/TN", InstallationPaths.ScheduledTaskName, "/F");
    }

    private static void RunScheduledTask(params string[] arguments) =>
        _ = RunScheduledTask(allowFailure: false, arguments);

    private static int RunScheduledTask(bool allowFailure, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Task Scheduler could not be started.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Task Scheduler failed with exit code {process.ExitCode}: " +
                $"{standardError.Trim()} {standardOutput.Trim()}".Trim());
        }

        return process.ExitCode;
    }

    private static void RegisterUninstaller()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath, writable: true)
            ?? throw new InvalidOperationException("The uninstall registry key could not be created.");
        var quotedApplicationPath = $"\"{InstallationPaths.ApplicationPath}\"";
        key.SetValue("DisplayName", InstallationPaths.ApplicationName);
        key.SetValue("DisplayVersion", InstallationPaths.Version);
        key.SetValue("Publisher", "WinNetSwitch");
        key.SetValue("InstallLocation", InstallationPaths.InstallDirectory);
        key.SetValue("DisplayIcon", InstallationPaths.ApplicationPath);
        key.SetValue("UninstallString", $"{quotedApplicationPath} --uninstall");
        key.SetValue("QuietUninstallString", $"{quotedApplicationPath} --uninstall-silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue(
            "EstimatedSize",
            (int)Math.Min(int.MaxValue, new FileInfo(InstallationPaths.ApplicationPath).Length / 1024),
            RegistryValueKind.DWord);
    }

    private static bool HasUninstallRegistryKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallRegistryPath);
        return key is not null;
    }

    private static string CreateAutostartTaskXml()
    {
        var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var escapedSid = SecurityElement.Escape(userSid);
        var escapedApplicationPath = SecurityElement.Escape(InstallationPaths.ApplicationPath);
        return $$"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Start WinNetSwitch in the interactive user session.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{{escapedSid}}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{{escapedSid}}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{{escapedApplicationPath}}</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static void CreateStartMenuShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows Script Host could not be created.");
            dynamic dynamicShell = shell;
            shortcut = dynamicShell.CreateShortcut(InstallationPaths.ShortcutPath);
            dynamic dynamicShortcut = shortcut;
            dynamicShortcut.TargetPath = InstallationPaths.ApplicationPath;
            dynamicShortcut.WorkingDirectory = InstallationPaths.InstallDirectory;
            dynamicShortcut.IconLocation = $"{InstallationPaths.ApplicationPath},0";
            dynamicShortcut.Description = "Switch physical network adapters";
            dynamicShortcut.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                _ = Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                _ = Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void StopInstalledProcesses()
    {
        foreach (var process in Process.GetProcessesByName("WinNetSwitch"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (!string.Equals(
                            processPath,
                            InstallationPaths.ApplicationPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string? newFileName,
        int flags);
}
