Autoclicker 1.5.1 adds click activity feedback and improves mouse press timing.

- A small green dot beside the status pulses for each mouse press accepted by Windows. This indicates sent input, not confirmation that the game registered it.
- Separates mouse-down and mouse-up with a short hold (20 ms at the default 50 ms interval), so applications that sample button state have time to observe the press.
- Keeps a brief released interval between presses, including after a delayed timer tick, without blocking the interface or moving the cursor.
- Releases a held click when you toggle off with F11, press Stop, reach the stop timer, move onto Autoclicker, or close the app.
- Preserves the default 20-clicks-per-second target, timing variation, custom hotkeys, and crosshair settings. Actual timing depends on Windows scheduling; game compatibility is not guaranteed.

Choose **Check for updates → Confirm update → Restart to update**. Versions before 1.3.0 apply on close and need to be reopened manually. You can also download **autoclicker.exe** directly; it is still one portable file.
