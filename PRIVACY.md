# Privacy policy

Autoclicker is a local Windows application maintained by [Syn1x](https://github.com/Syn1x). This policy describes version 1.5.0 and was last updated on September 21, 2026.

## Local operation

Mouse clicking, the stop timer, hotkey detection, and the crosshair operate on your computer. The application does not upload keyboard input, mouse positions, click counts, screen content, or game activity. It contains no advertising, usage analytics, account system, or remote crash reporting.

The selected hotkey and crosshair appearance are stored under `%LOCALAPPDATA%\Autoclicker`. Update downloads, replacement instructions, and completion/error messages are stored temporarily under `%TEMP%\Autoclicker-updates`. Update instructions can contain the executable's local path and process information; these remain on your computer. Completed update folders are cleaned up on a later launch.

Closing the application stops clicking, removes the overlay, and releases the keyboard hook. To remove the portable app, close it and delete its EXE. You may also delete the settings and temporary folders above to remove its remaining local data.

## Update requests

The app automatically requests the latest update manifest from this repository's GitHub Releases when it starts. Clicking **Check for updates** makes the same request. Clicking **Confirm update** downloads the selected release executable from GitHub; it is not downloaded before confirmation.

GitHub and its download infrastructure receive ordinary connection information, including your IP address and request time. The app also sends a User-Agent header containing `Autoclicker` and its current version. It does not send your saved preferences, click history, or a unique device identifier. These requests are covered by [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

Clicking and the crosshair remain usable without an internet connection. Version 1.5.0 does not have an in-app switch to disable automatic update checks.

## Signing and support

SignPath would process release builds during development if signing is approved; the running Autoclicker application does not contact SignPath.

Questions can be raised through the [project's GitHub issues](https://github.com/Syn1x/autoclicker/issues). Issues are public, so avoid including private information.
