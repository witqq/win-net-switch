# SignPath Foundation onboarding

**English** | [Русский](SIGNPATH_ONBOARDING.ru.md)

This document covers maintainer-owned setup. It does not authorize an agent or contributor to submit an application, install a GitHub App, change repository access, or handle a token.

## Verified project readiness

WinNetSwitch is a public, actively maintained MIT-licensed project with published Windows releases, source-controlled build scripts, CI, tests, user documentation, a privacy policy, a security policy, and a public [Code Signing Policy](../CODE_SIGNING_POLICY.md). The repository contains project source plus redistributable open-source dependencies with license notices; it does not intentionally include a proprietary WinNetSwitch component.

SignPath Foundation makes the eligibility decision. Repository preparation does not imply acceptance or guarantee that SmartScreen warnings immediately disappear.

## Maintainer steps

- Enable multi-factor authentication on the GitHub account and the SignPath account.
- Review the current [SignPath Foundation conditions](https://signpath.org/terms.html).
- Submit the project through the [SignPath Foundation application](https://signpath.org/) using:
  - project: `WinNetSwitch`;
  - repository: `https://github.com/witqq/win-net-switch`;
  - latest release: `https://github.com/witqq/win-net-switch/releases/latest`;
  - license: `https://github.com/witqq/win-net-switch/blob/main/LICENSE`;
  - code-signing policy: `https://github.com/witqq/win-net-switch/blob/main/CODE_SIGNING_POLICY.md`;
  - privacy: `https://github.com/witqq/win-net-switch/blob/main/PRIVACY.md`;
  - security: `https://github.com/witqq/win-net-switch/blob/main/SECURITY.md`.
- After acceptance, install the SignPath GitHub App only for `witqq/win-net-switch` and connect the repository as the trusted build system.
- In SignPath, confirm the project, artifact configuration, release signing policy, trusted GitHub build system, origin verification, approver, and SignPath Foundation certificate assigned by the service.
- Create a dedicated CI submitter/API token with only the permission required to submit this project's signing requests.
- In GitHub **Settings** → **Secrets and variables** → **Actions**, store the token as the repository secret `SIGNPATH_API_TOKEN`. Never paste its value into chat, an issue, a workflow input, or a source file.
- Store non-secret identifiers as repository variables:
  - `SIGNPATH_ORGANIZATION_ID`;
  - `SIGNPATH_PROJECT_SLUG`;
  - `SIGNPATH_SIGNING_POLICY_SLUG`;
  - `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`.
- Provide the non-secret endpoint and identifiers to the release-pipeline maintainer; confirm only that the secret exists, never its value.

## Integration acceptance checks

Do not call signing setup complete until a new immutable release proves all of the following:

- the artifact submitted to SignPath originates from the tagged GitHub-hosted workflow;
- the inner `WinNetSwitch.exe` is signed before the installer embeds it;
- the final installer is signed afterward;
- `Get-AuthenticodeSignature` and `signtool verify /pa /all /v` succeed for both release EXEs;
- both signatures contain an RFC 3161 timestamp using SHA-256;
- checksums are generated only after signing;
- changing or failing any signing step prevents release publication;
- the published files and `SHA256SUMS.txt` have matching hashes.

Follow the current official [SignPath GitHub integration guide](https://docs.signpath.io/trusted-build-systems/github) when implementing the pipeline.
