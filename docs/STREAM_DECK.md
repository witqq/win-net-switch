# Stream Deck Plugin and Marketplace Guide

**English** | [Русский](STREAM_DECK.ru.md)

## Architecture and dependency

The Stream Deck plugin is a non-elevated Node.js client. It sends bounded JSON requests to the elevated WinNetSwitch tray application through the local `WinNetSwitch.Control.v1` named pipe. The pipe grants access to the current Windows logon SID when that SID is present in the application token; elevated Task Scheduler tokens that omit it safely fall back to the current user SID. A medium mandatory-integrity label lets the ordinary Stream Deck process connect, and the pipe never grants access to `Everyone`. Adapter discovery and mutations remain in `PhysicalNetworkAdapterService`; the plugin does not invoke PowerShell or Windows network APIs directly.

WinNetSwitch is a mandatory, separately installed companion. The `.streamDeckPlugin` package must not contain `WinNetSwitch.exe`, the installer, a DLL, MSI, PowerShell script, batch file, or command script. The plugin's Property Inspector and manifest provide these links:

- [download WinNetSwitch](https://github.com/witqq/win-net-switch/releases/latest/download/WinNetSwitch-Setup.exe);
- [support and bug reports](https://github.com/witqq/win-net-switch/issues);
- [privacy policy](../PRIVACY.md).

## User installation

1. Install and start the latest WinNetSwitch companion. Its tray icon must be present.
2. Install Stream Deck 7.1 or later on Windows 10 or later.
3. Download `dev.witqq.win-net-switch.streamDeckPlugin` from the latest GitHub Release.
4. Double-click the file and approve installation in Stream Deck.
5. Drag `Adapter On/Off` or `Cycle Adapters` from the WinNetSwitch category to a key.
6. For `Adapter On/Off`, select a physical adapter in the Property Inspector. Use its refresh control after hardware changes.

Run Stream Deck normally, not as administrator. WinNetSwitch alone remains elevated because adapter mutations require administrator rights. Successful mutations display a green Stream Deck confirmation and a Windows tray notification; failures display an alert and a Windows error notification. Repeated Property Inspector requests within two seconds share one adapter query, and the cache is invalidated after every mutation.

`Adapter On/Off` changes only the selected adapter. `Cycle Adapters` sorts physical adapters by display name without case sensitivity, selects the item after the first active adapter, wraps after the last item, and calls the transactional enable-only operation. If none is active, it selects the first item.

Disabling an adapter can interrupt downloads and remote sessions. Do not test a mutation on the adapter that carries the current remote connection.

If a key says `Start WinNetSwitch`, confirm that the current companion is running and inspect `%APPDATA%\Elgato\StreamDeck\Plugins\dev.witqq.win-net-switch.sdPlugin\logs`. Do not work around a connection failure by permanently elevating Stream Deck; report the companion and plugin versions with sanitized log excerpts.

## Local development

Required tools:

- Node.js 24 installed through `nvm`, `nvm-windows`, or another version manager;
- npm;
- Stream Deck 7.1 or later for interactive device testing;
- the .NET 10 SDK for companion development.

From the repository root, run `nvm use` on macOS/Linux so NVM reads `.nvmrc`; with nvm-windows, run `nvm use 24.20.0`. Then run the following from `stream-deck-plugin`:

```powershell
npm ci
npm run typecheck
npm test
npm run package
```

`npm run package` builds the TypeScript entry point with Rolldown and invokes the official Elgato CLI. The output is `artifacts\stream-deck\dev.witqq.win-net-switch.streamDeckPlugin`.

To link a development directory to a local Stream Deck installation, run the following only on a development machine:

```powershell
streamdeck link .\dev.witqq.win-net-switch.sdPlugin
```

The repository does not link or install the plugin during CI. `scripts\test-stream-deck-package.ps1` opens the package as ZIP, checks its required entries, and rejects companion executables and scripts.

## Marketplace submission

Marketplace publication is manual through [Elgato Maker Console](https://maker.elgato.com/). Before submission:

- use Maker organization `witqq` and keep manifest `Author` identical;
- upload the generated `.streamDeckPlugin`, not the source directory;
- set the product as Windows-only and declare WinNetSwitch as a required external dependency;
- include the companion download, support, source, and privacy links;
- provide the product name, English description, release notes, and pricing choice;
- provide a 1920 × 960 PNG thumbnail and at least three 1920 × 960 PNG gallery images, or supported 1920 × 1080 videos;
- test the DRM-processed build downloaded from Maker Console before publication;
- demonstrate both actions on a real Stream Deck or Stream Deck Mobile without exposing private adapter data.

The immutable plugin UUID is `dev.witqq.win-net-switch`. Action UUIDs are `dev.witqq.win-net-switch.toggle-adapter` and `dev.witqq.win-net-switch.cycle-adapters`; do not change them after publication.

Use the current official [distribution guide](https://docs.elgato.com/streamdeck/sdk/introduction/distribution/), [plugin guidelines](https://docs.elgato.com/guidelines/stream-deck/plugins/), [submission guide](https://docs.elgato.com/maker-console/submitting-products/), and [review process](https://docs.elgato.com/maker-console/review-process/) as the final source of Marketplace requirements.
