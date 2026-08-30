# Releasing a new version

**English** | [Русский](RELEASING.ru.md)

WinNetSwitch releases are created by GitHub Actions from an existing annotated `vMAJOR.MINOR.PATCH` Git tag. The workflow does not modify source code and stops if the tag does not match the version in `Directory.Build.props`.

## Preparation

1. Switch to `main`, fetch the latest changes, and confirm that the working tree is clean.
2. Select the new version according to Semantic Versioning.
3. Update the same version in four sources:
   - `Directory.Build.props` — `Version`;
   - `src\WinNetSwitch.Windows\InstallationPaths.cs` — `InstallationPaths.Version`;
   - `src\WinNetSwitch.App\app.manifest` — `assemblyIdentity`;
   - `src\WinNetSwitch.Setup\app.manifest` — `assemblyIdentity`.
4. Update user documentation when behavior or requirements changed.
5. If the Stream Deck plugin changed, increment its four-component version in `stream-deck-plugin\dev.witqq.win-net-switch.sdPlugin\manifest.json`. Its version is independent from the companion application version.

A positive search must find the new version in every expected file:

```powershell
rg -n "1\.4\.2" Directory.Build.props src\WinNetSwitch.Windows src\WinNetSwitch.App\app.manifest src\WinNetSwitch.Setup\app.manifest
```

Replace `1.4.2` with the actual release version.

## Verification and publication

Run the complete local verification from an elevated PowerShell session:

```powershell
.\scripts\verify.ps1
```

Commit the version change, push `main`, and wait for the `CI` workflow to pass. Only then create the tag:

```powershell
git tag -a v1.4.2 -m "WinNetSwitch 1.4.2"
git push origin v1.4.2
```

The Release workflow:

1. validates the tag format and project version;
2. restores .NET and npm dependencies, builds the solution and plugin, and runs all tests and native smoke checks;
3. creates self-contained `WinNetSwitch.exe` and `WinNetSwitch-Setup.exe` files and the validated `dev.witqq.win-net-switch.streamDeckPlugin` package;
4. creates `SHA256SUMS.txt`;
5. publishes a GitHub Release with generated release notes.

## Published release verification

Confirm that the Release contains exactly four files:

- `WinNetSwitch-Setup.exe`;
- `WinNetSwitch.exe`;
- `dev.witqq.win-net-switch.streamDeckPlugin`;
- `SHA256SUMS.txt`.

Download `SHA256SUMS.txt` and compare it with the SHA-256 digest of both EXE files and the Stream Deck package. Run `scripts\test-stream-deck-package.ps1` against the downloaded plugin to confirm that no companion executable was embedded. Confirm that the Release is neither a draft nor a prerelease and points to the expected tag.

Never move or reuse a published tag. If a release is defective, fix the source, increment the patch version, and publish a new tag.
