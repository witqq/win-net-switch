# WinNetSwitch Marketplace submission

Use these values in Elgato Maker Console. Keep **Automatically publish after approval** disabled for the first submission so the Maker-processed package can be tested before release.

## Files

- Product type: `Stream Deck plugin`
- Plugin: `artifacts/stream-deck/dev.witqq.win-net-switch.streamDeckPlugin`
- App icon: `stream-deck-plugin/marketplace/media/app-icon.png`
- Thumbnail: `stream-deck-plugin/marketplace/media/thumbnail.png`
- Gallery 1: `stream-deck-plugin/marketplace/media/gallery-01-toggle.png`
- Gallery 2: `stream-deck-plugin/marketplace/media/gallery-02-cycle.png`
- Gallery 3: `stream-deck-plugin/marketplace/media/gallery-03-local-control.png`

The initial submission uses three gallery images and no video. Maker Console accepts three images as the required gallery set. Elgato review may still request a functional demonstration video because the plugin works with Stream Deck hardware; provide one as a revision if requested.

## Details

- Product name: `WinNetSwitch`
- Maker / manifest Author: `witqq`
- Price: `Free`
- Platform: `Windows`
- Compatibility: `Windows 10 or 11; Stream Deck 7.1 or later; keypad keys`
- Category / tags: select the closest available `Productivity` or `Utilities` tag plus `Windows`; do not select macOS, dial, or touch-strip support.

### Short description

Control physical Windows network adapters from Stream Deck while keeping Stream Deck itself at ordinary user rights.

### Full description

Control physical Wi-Fi and Ethernet adapters from Stream Deck on Windows. The free WinNetSwitch companion App must be installed and running; Stream Deck itself remains at ordinary user rights. Adapter On/Off changes only a selected adapter, while Cycle Adapters enables the next physical adapter and disables the others. Requires Windows 10 or 11 and Stream Deck 7.1 or later. Every change provides key feedback and a local Windows notification.

### External dependency

Requires the free WinNetSwitch companion App. Install and start it before using the plugin. WinNetSwitch runs elevated for Windows adapter operations; Stream Deck must not be run as administrator.

### Release notes

Initial Marketplace release.

- Toggle one selected physical network adapter without changing the others.
- Cycle exclusively through available physical adapters.
- Show adapter state, success, and actionable errors on the key.
- Connect locally to the separately installed WinNetSwitch companion App.
- Require Windows 10 or 11, Stream Deck 7.1 or later, and WinNetSwitch 1.4.2 or later.

## Additional links

- Download companion: `https://github.com/witqq/win-net-switch/releases/latest/download/WinNetSwitch-Setup.exe`
- Setup guide: `https://github.com/witqq/win-net-switch/blob/main/docs/STREAM_DECK.md`
- Support: `https://github.com/witqq/win-net-switch/issues`
- Source: `https://github.com/witqq/win-net-switch`
- Privacy: `https://github.com/witqq/win-net-switch/blob/main/PRIVACY.md`
- Security: `https://github.com/witqq/win-net-switch/security/policy`

## Before upload

- Confirm the Maker organization is exactly `witqq`, matching the manifest Author.
- Confirm the organization support method and current Maker Agreement in Maker Console.
- Confirm product name `WinNetSwitch` and monetization `Free`; Maker Console cannot change them without Elgato support.
- Rebuild and validate the plugin from the clean accepted revision.
- Verify every link above returns a successful response.
- Confirm all media is English, contains no real adapter identifiers or Wi-Fi names, and uses only project-owned artwork.
- Upload with automatic publication disabled.
- Download and test the Maker-processed plugin before submitting it for review.
