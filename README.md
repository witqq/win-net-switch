# WinNetSwitch

**English** | [Русский](README.ru.md)

[![CI](https://github.com/witqq/win-net-switch/actions/workflows/ci.yml/badge.svg)](https://github.com/witqq/win-net-switch/actions/workflows/ci.yml)
[![GitHub release](https://img.shields.io/github/v/release/witqq/win-net-switch)](https://github.com/witqq/win-net-switch/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

WinNetSwitch is a small Windows 10/11 system tray application for controlling physical network adapters. It can independently enable or disable Wi-Fi, Ethernet, and other physical interfaces, or leave only one selected adapter enabled.

The application interface, installer, errors, and diagnostic messages are in English. Complete Russian documentation is available through the language link above.

## Download

| File | Purpose |
|---|---|
| [WinNetSwitch-Setup.exe](https://github.com/witqq/win-net-switch/releases/latest/download/WinNetSwitch-Setup.exe) | Recommended installer with automatic startup and standard uninstallation |
| [WinNetSwitch.exe](https://github.com/witqq/win-net-switch/releases/latest/download/WinNetSwitch.exe) | Portable self-contained build without installation or automatic startup |
| [SHA256SUMS.txt](https://github.com/witqq/win-net-switch/releases/latest/download/SHA256SUMS.txt) | Checksums for the published files |

All versions and release notes are available on the [GitHub Releases](https://github.com/witqq/win-net-switch/releases) page.

## Installation

1. Download `WinNetSwitch-Setup.exe` using the link above.
2. Optionally verify its SHA-256 checksum as described below.
3. Run the installer and approve the Windows User Account Control (UAC) prompt.
4. Confirm the installation in the WinNetSwitch dialog.
5. Find the application icon in the system tray. Windows may place it in the hidden icons area next to the clock.

The installer:

- copies the application to `%LOCALAPPDATA%\Programs\WinNetSwitch`;
- adds a Start menu shortcut;
- registers WinNetSwitch in Windows Installed apps;
- creates an automatic startup task for the current user;
- immediately starts the application in the interactive user session.

### SmartScreen warning

The release files are not currently signed with a commercial code-signing certificate. Microsoft Defender SmartScreen may therefore warn that the publisher is unknown.

Continue only if you downloaded the file from the [official GitHub Release](https://github.com/witqq/win-net-switch/releases/latest) and its SHA-256 matches `SHA256SUMS.txt`. After verifying the file, select **More info** → **Run anyway** in the SmartScreen dialog.

### Verify SHA-256

Place the installer and `SHA256SUMS.txt` in the same directory, open PowerShell there, and run:

```powershell
$expected = (Get-Content .\SHA256SUMS.txt |
    Where-Object { $_ -match '  WinNetSwitch-Setup\.exe$' } |
    ForEach-Object { ($_ -split '\s+')[0] })
$actual = (Get-FileHash .\WinNetSwitch-Setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
$actual -eq $expected
```

`True` means the checksum matches. Replace the file name with `WinNetSwitch.exe` to verify the portable build.

## Usage

WinNetSwitch has no main window. It runs exclusively through its system tray icon.

1. Right-click the WinNetSwitch tray icon.
2. Point to the adapter you want to control.
3. Select one of the actions:
   - `Enable` or `Disable` changes only the selected adapter;
   - `Enable only this adapter` enables the selected adapter and disables every other physical adapter.

A check mark next to an adapter indicates its final enabled state. For Wi-Fi, this includes both the device state and the software Wi-Fi radio state.

The adapter list refreshes in the background. While the menu is open, its items are kept stable so the focus is not reset; refreshed state appears after closing and reopening the menu. Use `Refresh` or double-click the tray icon to request a manual refresh.

> **Important:** disabling an active adapter immediately interrupts its network connections, downloads, and remote sessions. `Enable only this adapter` intentionally disables every other physical interface. Do not use it on an adapter through which you are remotely controlling the computer.

## How Wi-Fi control works

WinNetSwitch combines active physical interfaces from `Get-NetAdapter -Physical` with administratively disabled PCI/USB Plug and Play (PnP) network devices. This keeps Wi-Fi visible in the menu after the adapter has been disabled or the computer has restarted. Virtual VPN, Hyper-V, WSL, and other software interfaces are intentionally excluded.

When enabling Wi-Fi, WinNetSwitch:

1. enables the PnP device if it disappeared from `Get-NetAdapter`;
2. waits for the network interface to appear;
3. enables the software radio through the Windows Native Wi-Fi API;
4. verifies the final state.

When disabling Wi-Fi, the application turns off the software radio before disabling the adapter. If an operation fails after a partial change, WinNetSwitch attempts to restore the original state and records the result in the log.

The Native Wi-Fi API cannot remove a hardware block. Airplane mode, a hardware switch, BIOS settings, or an organization policy may prevent Wi-Fi from being enabled. An enabled adapter also does not guarantee a connection to an access point: Windows selects and connects to saved Wi-Fi networks.

## Diagnostics

The application log is stored at:

```text
%LOCALAPPDATA%\WinNetSwitch\logs\WinNetSwitch.log
```

Open it from the tray menu with `Open error log`. When the log reaches 1 MiB, the previous file is rotated to `WinNetSwitch.previous.log`.

| Problem | What to check |
|---|---|
| The icon is missing after installation | Open the hidden icons area next to the clock, then try the WinNetSwitch Start menu shortcut |
| “WinNetSwitch is already running” appears | An existing instance is active; locate its tray icon |
| An adapter is missing | Only physical adapters are shown; virtual VPN, Hyper-V, and WSL interfaces are intentionally excluded |
| The Wi-Fi adapter was enabled but Wi-Fi stayed off | Check airplane mode, hardware switches, BIOS settings, and device policies, then inspect the log |
| Wi-Fi is enabled but not connected | Windows, not WinNetSwitch, connects to a saved access point |
| Switching takes several seconds | WinNetSwitch waits for Windows and verifies the final state; operation details are available in the log |
| The menu did not change during a refresh | This is expected: the open menu remains stable and applies refreshed state after it closes |
| An operation failed | Open the error log, reproduce the problem, and attach a sanitized excerpt to a bug report |

If the problem is reproducible, submit a [bug report](https://github.com/witqq/win-net-switch/issues/new?template=bug_report.yml). Do not publish passwords, tokens, MAC addresses, Wi-Fi network names, or other personal data.

## Uninstallation

Open **Settings** → **Apps** → **Installed apps**, find WinNetSwitch, and select **Uninstall**.

The uninstaller removes the application, automatic startup task, shortcut, Installed apps registration, and diagnostic logs. The portable build does not register an uninstaller: exit WinNetSwitch from its tray menu and delete the downloaded EXE. Remove `%LOCALAPPDATA%\WinNetSwitch` separately if you also want to delete portable-build logs.

## Requirements and limitations

- Windows 10 version 2004 (build 19041) or later, or Windows 11;
- local administrator privileges for adapter management;
- Windows PowerShell 5.1 and the built-in `NetAdapter` module;
- the public release targets Windows x64; build ARM64 locally with `scripts\publish.ps1 -Runtime win-arm64`;
- no installed .NET runtime is required because the application is self-contained.

Building from source requires the supported .NET 10 LTS SDK. The project has no third-party NuGet dependencies.

## Development

The solution contains five projects:

- `src\WinNetSwitch.Core` — models, PowerShell runner, Native Wi-Fi API, and transactional operations;
- `src\WinNetSwitch.App` — Windows Forms `ApplicationContext`, `NotifyIcon`, and tray menu;
- `src\WinNetSwitch.Windows` — installation, uninstallation, shortcut, and Task Scheduler startup;
- `src\WinNetSwitch.Setup` — self-contained GUI installer with an embedded payload;
- `tests\WinNetSwitch.Tests` — executable tests without an external test framework.

On Windows with the SDK specified by `global.json`, run:

```powershell
dotnet restore .\WinNetSwitch.slnx
dotnet build .\WinNetSwitch.slnx --configuration Release --no-restore
dotnet run --project .\tests\WinNetSwitch.Tests\WinNetSwitch.Tests.csproj --configuration Release --no-restore
```

The complete local verification requires an elevated PowerShell session. It performs a Release build, 15 tests, self-contained publication, a read-only probe of real adapters, the native tray smoke test, and installer payload validation:

```powershell
.\scripts\verify.ps1
```

Use `-SkipSmoke` in environments without an interactive desktop. Outputs are written to `artifacts\publish\win-x64` and `artifacts\setup\win-x64`.

GitHub Actions runs [CI](https://github.com/witqq/win-net-switch/actions/workflows/ci.yml) for `main` and pull requests. A `vMAJOR.MINOR.PATCH` tag starts the [Release workflow](https://github.com/witqq/win-net-switch/actions/workflows/release.yml), which rebuilds the project in the cloud and publishes the EXE, installer, and SHA-256 checksums.

Additional documentation:

- [contribution guide](CONTRIBUTING.md);
- [community code of conduct](CODE_OF_CONDUCT.md);
- [release guide](docs/RELEASING.md);
- [security policy](SECURITY.md);
- [MIT license](LICENSE).

## Project support

- Bug: [submit a bug report](https://github.com/witqq/win-net-switch/issues/new?template=bug_report.yml)
- Idea: [request a feature](https://github.com/witqq/win-net-switch/issues/new?template=feature_request.yml)
- Vulnerability: use a private [GitHub Security Advisory](https://github.com/witqq/win-net-switch/security/advisories/new), not a public issue

WinNetSwitch is distributed under the [MIT License](LICENSE).
