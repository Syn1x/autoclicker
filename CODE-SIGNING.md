# Code signing policy

## Current status

Autoclicker is preparing an application for free code signing through SignPath Foundation. Approval has not been granted and signing is not active. Existing releases, including v1.5.0, are unsigned.

If the application is approved, the signing service attribution will be: Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Scope and responsibilities

Only `autoclicker.exe` built from the source in [Syn1x/autoclicker](https://github.com/Syn1x/autoclicker) is in scope. The project is maintained by [Syn1x](https://github.com/Syn1x), who is the author, reviewer of outside contributions, and designated release-signing approver.

Production signing requires multi-factor authentication for both GitHub and SignPath. Each signing request must receive manual approval from the maintainer. Signing access is not granted to untrusted pull requests.

## Release process

The current GitHub Actions workflow builds and tests the portable executable on a GitHub-hosted Windows runner. Once signing is approved and configured, it will upload that build for SignPath origin verification, obtain maintainer approval, sign and timestamp the executable, verify the returned signature, and then generate the update manifest and checksums from the signed file before publishing it.

A failed or declined signing request must not publish a release as signed. Published executables must not be modified after signing. A signed version will be published under a new version number so existing users can receive it through the updater.

See the [privacy policy](PRIVACY.md) for the application's network requests and locally stored settings.
