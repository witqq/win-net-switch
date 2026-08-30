# Code Signing Policy

**English** | [Русский](CODE_SIGNING_POLICY.ru.md)

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Signed artifacts

The policy applies only to official `WinNetSwitch.exe` and `WinNetSwitch-Setup.exe` files built from this public repository by GitHub Actions for an immutable version tag. The separately distributed Stream Deck package is validated and checksummed but is not an Authenticode executable.

Signing must preserve this build order:

- build and test `WinNetSwitch.exe`;
- sign and verify `WinNetSwitch.exe`;
- embed that signed executable into `WinNetSwitch-Setup.exe`;
- sign and verify `WinNetSwitch-Setup.exe`;
- generate checksums and publish the release without modifying either signed file.

Signing is not active until the project has been accepted by SignPath Foundation and the release workflow contains an approved SignPath integration. Until then, release documentation explicitly identifies the binaries as unsigned.

## Project roles

- Committer and reviewer: [witqq](https://github.com/witqq).
- Signing approver: [witqq](https://github.com/witqq).

Changes from other contributors require review by the maintainer before merge. A release signing request requires approval by the signing approver and must originate from the repository's GitHub-hosted release workflow and version tag.

## Security controls

- Maintainers and signing approvers must use multi-factor authentication for GitHub and SignPath.
- The SignPath GitHub App is restricted to this repository.
- Release signing accepts only artifacts whose source and GitHub Actions build origin are verified by SignPath.
- Signing credentials are stored only as GitHub Actions secrets. Tokens and private keys must never appear in source files, workflow inputs, build logs, issues, pull requests, chat, or release assets.
- A signing failure stops publication. The workflow must never fall back to publishing an unsigned file under a release that claims to be signed.
- Vulnerabilities are reported privately through the [Security Policy](SECURITY.md).

## Privacy

WinNetSwitch does not transfer information to other networked systems unless the user explicitly requests a network action, such as opening a GitHub download or support link. Normal adapter control, local IPC, settings, and diagnostic logs remain on the user's Windows computer. See the complete [Privacy Policy](PRIVACY.md).

SignPath and GitHub process build, repository, identity, and signing-request data as independent service providers when maintainers use their services. Their respective privacy terms apply to that maintainer-operated release process.

## Verifying a release

After signing is enabled, users can inspect either EXE in Windows **Properties** → **Digital Signatures** or run:

```powershell
Get-AuthenticodeSignature .\WinNetSwitch.exe |
    Select-Object Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

The signature status must be `Valid`. Users should also compare the file's SHA-256 value with the release's `SHA256SUMS.txt`.
