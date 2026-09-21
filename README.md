# Autoclicker

A Windows autoclicker with configurable keyboard control, a stop timer, sound cues, and automatic updates.

## Download

Download [**autoclicker.exe**](https://github.com/Syn1x/autoclicker/releases/latest/download/autoclicker.exe). Install once; future releases download automatically when the app opens and install when it closes. A portable ZIP is also available from [the latest release](https://github.com/Syn1x/autoclicker/releases/latest): extract the entire ZIP and keep its files together.

Windows 10/11, x64, and .NET Framework 4.8 are required. Setup can install the framework if necessary. Current releases are unsigned; Windows may display an unknown-publisher warning.

## Use

- Place your cursor over the desired target and press **F11** to start or stop. Use **Change key** to bind a different keyboard key.
- Starting is hotkey-only. The red **Stop** button can stop clicking.
- **Stop after** defaults to one minute. Enter minutes and seconds, or uncheck it for continuous clicking.
- The default click interval is 50 ms, with optional ±10% timing variation.
- Rising and falling sounds indicate start and stop.
- Moving over this app's window or closing it stops clicking. Closing exits the application.

The selected key is stored outside the versioned app directory and survives updates. Other click/timer options reset to their defaults each launch. This sends ordinary Windows left mouse input; it does not detect team slots or game state.

## Updates

The app checks this repository's stable GitHub Releases on launch. **Check for updates** checks manually. Downloads run asynchronously; failures leave the current version usable. Velopack verifies downloaded package checksums. Prepared updates install only after the window, keyboard hook, click timer and audio resources have closed. The short-lived updater exits afterward; no permanent background service is installed.

People using the original standalone EXE need to download this packaged release once. Copying only the new inner EXE does not include its dependencies or updater.

## Build and test

Install the .NET 9 SDK (or a compatible newer SDK) and use PowerShell on Windows:

```powershell
./scripts/build.ps1
./scripts/build.ps1 -Version 1.0.3 -Package
```

The script restores locked dependencies, builds, runs non-clicking tests, and optionally packages an installer, portable ZIP, update packages and feed. Build files go into `artifacts/`, or a directory supplied with `-BuildRoot`.

## Publish an update

1. Edit `RELEASE-NOTES.md` to describe the new release.
2. Commit and push the code and release notes to `main`.
3. In **Actions → Publish release → Run workflow**, enter a new version such as `1.0.3`.
4. The workflow tests, packages and publishes the installer and update feed. Existing users receive it on their next update check.

The workflow uses GitHub's temporary repository token. The distributed app contains no GitHub credentials and downloads public releases without a login. Publishing a newer version is the update trigger; a normal code push only builds and tests.

### Signing

A publisher code-signing certificate has not been configured. To sign on a build machine with a trusted certificate available, pass SignTool options with `-SigningParameters` (or `AUTOCLICKER_SIGN_PARAMS`). Velopack signs the packaged executable and installer. Keep certificates and credentials in protected stores or CI secrets, never in the repository. Do not share a self-signed certificate as a substitute for a trusted publisher certificate.

Updater documentation: [Velopack](https://docs.velopack.io/).
