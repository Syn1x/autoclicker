# Autoclicker

A single portable Windows EXE with configurable keyboard control, a stop timer, sound cues, a customizable crosshair overlay, and automatic updates.

## Download and run

Download [**autoclicker.exe**](https://github.com/Syn1x/autoclicker/releases/latest/download/autoclicker.exe) and double-click it. That is the complete app. You can move it or send that one file to someone else.

There is no installer, ZIP to extract, application folder, shortcut creation, or background service. Keep the EXE in a writable location such as Downloads or Desktop so it can replace itself when an update is ready. Windows 10/11 with .NET Framework 4.8 is required. Releases are currently unsigned.

**Upgrading from 1.0.x:** close the old app and download this EXE once. The previous installed/ZIP editions use a different update system. Version 1.1.0 and later update the standalone EXE directly.

## Use

- Place your cursor over the desired target and press **F11** to start or stop. Use **Change key** to bind a different keyboard key.
- Starting is hotkey-only. The red **Stop** button can stop clicking.
- **Stop after** defaults to one minute. Enter minutes and seconds, or uncheck it for continuous clicking.
- The default click interval is 50 ms, with optional ±10% timing variation.
- Rising and falling sounds indicate start and stop.
- Moving over this app's window or closing it stops clicking. Closing exits the application.
- Toggle **Crosshair** in the top bar to show an independent center-screen overlay. **Customize** opens its appearance controls.
- Number fields and dropdown text are centered. Typed numbers apply when you click elsewhere, leave the field, switch windows, or close settings; Enter also works.

The selected key is saved in `%LOCALAPPDATA%\Autoclicker\Autoclicker.settings`, outside the EXE, so it survives updates and does not travel with the file you share. The previous edition's key is imported if available. Other click/timer options reset to their defaults each launch. This sends ordinary Windows left mouse input; it does not detect team slots or game state.

## Crosshair

Choose **Dot**, **Cross**, **Open cross**, **Circle**, or **Circle + dot**. Adjust the pixel size, move the rainbow color slider, or select **White**. The optional dark outline helps the crosshair stay visible on bright scenes. The display selector centers it on your chosen monitor; it uses the full screen, not the area above the taskbar.

The overlay lets mouse input pass through and never takes keyboard focus. It works independently of the autoclicker, remains visible when Autoclicker is minimized, and disappears when the app closes. It starts off each launch, with your last appearance saved separately beside your keyboard preferences. Only changes to its appearance or display trigger a redraw; it has no continuous rendering loop.

Use windowed or borderless games. Exclusive fullscreen games and other protected surfaces may hide ordinary desktop overlays. This is a desktop window, with no game injection or interaction with game memory.

## Updates

The app checks this repository's `autoclicker-update.json` release asset on launch. **Check for updates** checks manually. A newer standalone EXE downloads in the background and is checked against the release's SHA-256 hash, size, assembly identity, and version. A failed or cancelled download leaves the current app usable.

A verified update applies when you close the app. Windows cannot replace a running EXE, so the app temporarily copies itself into `%TEMP%\Autoclicker-updates` as a helper. The helper waits for the original process to exit, verifies the update again, and atomically replaces the same EXE in its existing location. It refuses to overwrite a file that changed after the update was prepared. It exits after the operation; the next launch cleans up the temporary helper files. No permanent updater is installed.

The EXE needs write permission to its folder to update. A read-only location keeps the existing version; move the EXE to a writable folder and check again. Close other running copies before reopening after an update.

Only `autoclicker.exe` is needed by users. The JSON manifest and checksum list on the release page support the updater and download verification; they do not need to be downloaded manually.

## Build and test

Install the .NET 9 SDK (or a compatible newer SDK) and use PowerShell on Windows:

```powershell
./scripts/build.ps1
./scripts/build.ps1 -Version 1.2.1 -Package
```

The script restores locked build dependencies, builds, runs non-clicking tests, and optionally creates `autoclicker.exe`, `autoclicker-update.json`, and `SHA256SUMS.txt`. Build files go into `artifacts/`, or a directory supplied with `-BuildRoot`. The distributed EXE has only Windows/.NET Framework dependencies and embeds its icon; it requires no adjacent DLL or configuration file.

Tests exercise an EXE-only launch, the real updater helper and parent-exit wait, checksum/version validation, failed and cancelled downloads, locked or changed targets, custom filenames, helper cleanup, and the keyboard/timer/UI behavior. Crosshair tests cover rendering, color controls, persisted appearance, monitor coordinates, native click-through and focus behavior, minimizing, and cleanup on close. They send no real mouse or keyboard input.

## Publish an update

1. Edit `RELEASE-NOTES.md` to describe the new release.
2. Commit and push the code and release notes to `main`.
3. In **Actions → Publish release → Run workflow**, enter a newer version such as `1.2.2`.
4. The workflow tests and publishes the standalone EXE and its update manifest. Users receive it on their next update check.

The workflow uses GitHub's temporary repository token. The EXE contains no GitHub credentials. Update URLs are restricted to this repository; the manifest cannot redirect the app to an arbitrary download host. Public HTTPS release assets and their checksum manifest are the update trust source. A publisher signing certificate has not been configured.
