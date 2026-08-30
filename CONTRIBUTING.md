# Contributing to WinNetSwitch

**English** | [Русский](CONTRIBUTING.ru.md)

Thank you for your interest in WinNetSwitch.

Before changing code, read the [Code of Conduct](CODE_OF_CONDUCT.md) and search the [existing issues](https://github.com/witqq/win-net-switch/issues). Use the [bug report](https://github.com/witqq/win-net-switch/issues/new?template=bug_report.yml) for defects and the [feature request](https://github.com/witqq/win-net-switch/issues/new?template=feature_request.yml) for new behavior. Do not disclose vulnerabilities in issues; follow the [Security Policy](SECURITY.md).

## Development environment

Building requires Windows 10/11 and the .NET SDK specified by `global.json`. The project has no third-party NuGet dependencies.

```powershell
dotnet restore .\WinNetSwitch.slnx
dotnet build .\WinNetSwitch.slnx --configuration Release --no-restore
dotnet run --project .\tests\WinNetSwitch.Tests\WinNetSwitch.Tests.csproj --configuration Release --no-restore
```

The complete verification includes self-contained publication, the native tray smoke test, and installer payload validation. Run it from an elevated PowerShell session:

```powershell
.\scripts\verify.ps1
```

## Changes

- Do not add secrets, real network identifiers, user-specific paths, or diagnostic logs.
- Add a test for network logic changes that distinguishes the required behavior from a superficially similar incorrect state.
- Do not remove final-state verification or transactional rollback to make an operation appear faster.
- Run the Release build and tests before opening a pull request.

Use imperative commit messages with a `feat:`, `fix:`, `docs:`, `test:`, `build:`, or `chore:` prefix.

## Pull requests

1. Create a dedicated branch from the current `main`.
2. Implement one logically complete change together with its tests and documentation.
3. Run the verification commands above.
4. Open a pull request and complete its checklist.
5. Wait for the GitHub Actions `CI` workflow to pass.

Do not include generated `artifacts`, local logs, or environment files in a pull request. Maintainers should follow the [release guide](docs/RELEASING.md) when publishing a version.
