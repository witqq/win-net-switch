# WinNetSwitch Privacy Policy

**English** | [Русский](PRIVACY.ru.md)

WinNetSwitch and its Stream Deck plugin operate locally on the user's Windows computer. They do not include analytics, advertising, telemetry, user accounts, or a project-operated network service.

## Data processed locally

The WinNetSwitch companion reads the following Windows network-adapter information to display and change adapter state:

- adapter display name, description, interface identifier, status, and enabled state;
- Plug and Play device identifier when Windows requires it to re-enable a disabled physical adapter;
- software and hardware Wi-Fi radio state.

The Stream Deck plugin receives only the adapter identifier, display name, description, status, enabled state, active state, and wireless flag through a local named pipe restricted to the current interactive Windows logon session. Stream Deck stores the identifier of the adapter selected for an `Adapter On/Off` action as that action's local setting.

## Logs

WinNetSwitch writes local diagnostic logs to `%LOCALAPPDATA%\WinNetSwitch\logs`. Logs can contain adapter names, interface identifiers, operation results, and Windows error messages. The application rotates its log at 1 MiB. Do not publish a log before reviewing and removing personal or environment-specific information.

The Stream Deck SDK may write its own local plugin logs under Stream Deck's plugin storage. WinNetSwitch does not transmit these logs.

## Network access

Normal adapter control uses local Windows APIs, Windows PowerShell, and local inter-process communication. WinNetSwitch and the plugin do not send adapter data to the project maintainers or any third party.

The plugin opens GitHub only when the user explicitly selects its Download, Support, or manifest help link. GitHub then processes the request under GitHub's own privacy terms.

## Removal and support

The WinNetSwitch uninstaller removes the application and its diagnostic data. Stream Deck manages plugin installation and action settings; remove the plugin and its actions through Stream Deck to stop their use.

Privacy questions can be submitted through [WinNetSwitch support](https://github.com/witqq/win-net-switch/issues). Do not include private network information in a public issue.
