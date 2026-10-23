# Troubleshooting

[Deutsch](../anleitung/hilfe.md) · **English**

[Windows](#windows) · [Android](#android) · [Kodi](#kodi) · [Portable version](#portable-version) ·
[Diagnostics and bug reports](#diagnostics-and-bug-reports)

## Windows

### The Switch 2 controller does not connect

- Press the SYNC button only briefly, do not hold it.
- If the controller is listed in the Windows Bluetooth settings, remove it there. SKZ Connect removes such a pairing
  itself, but it interferes as long as it exists.
- Put a nearby Switch 2 into sleep mode so it does not take over the controller.
- If the adapter has no Bluetooth LE, Switch 2 controllers only work via USB cable (Pro Controller 2, GameCube
  controller).

### A Switch, Wii or NSO controller is not found

Choose *Search for controllers …* on the Controllers page or *Pair controller …* in the notification area menu, then
press the SYNC button while the search is running. The automatic search pauses while someone is playing and leaves a
controller alone for 90 seconds after a failed attempt; the window tries anyway.

### Steam or a game shows a controller twice

Turn on *General › Output › Hide original controllers*, confirm the Windows prompt and restart Steam once. For a
single controller: card › Settings › Extras › *Shown twice?*. If HidHide is missing, *General* offers the
*Install HidHide …* button.

### A Joy-Con is suddenly a separate player

It was split from the pair (held sideways while SL or SR was pressed). Press L on the left and R on the right Joy-Con
at the same time to make them a pair again.

### “ViGEmBus driver missing” message

Without this driver, no virtual controller is created. Run the setup again; for the portable version, install
[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases). ViGEmBus has not been developed further since 2023. If
Windows no longer loads the driver after an update, please report it with diagnostics.

### Weak connection or many controllers at once

- If a controller constantly delivers fewer than 20 reports per second, SKZ Connect shows a notice. Reduce the distance
  to the adapter; do not use a USB Bluetooth dongle right next to USB 3 devices (an extension cable helps).
- Simple Bluetooth dongles often handle only two or three controllers at once. For more, use a more capable adapter,
  such as an Intel AX200/AX210 or a dongle with Bluetooth 5.3.

### Emulators get no motion data

*General › Advanced › Gyro for emulators (DSU)* must be on; a change takes effect after restarting SKZ-Connect. If
another program already uses port 26760 (such as DS4Windows), SKZ Connect runs without DSU and notes this in the log.

### “Settings restored” or “Settings damaged” message

The `settings.json` file could not be read. SKZ Connect loaded the last backup (`settings.json.bak`) or the defaults
and kept the unreadable file as `settings.json.unlesbar-<date>`. Clicking the message shows the file in Explorer.

### SmartScreen warns at startup

Setup and app are not code-signed. Continue with “More info” › “Run anyway”. The complete source code is in this
repository.

## Android

### The controller is not found

Turn on Bluetooth and hold the SYNC button until the lights run. If the controller is currently connected to a Switch
or a PC, turn off Bluetooth there first.

### A Switch 1 controller or a clone is not found

The app supports only Switch 2 controllers. Pair older Switch controllers and clones in Android's Bluetooth settings;
then usually only buttons and sticks work.

### Connected, but the game does not react

The Controllers page must say “Ready” at the top. If it says that Shizuku is not running, open Shizuku and tap *Start*
(needed after every device restart).

### The connection drops while playing

Exclude SKZ Connect from battery optimization: *Settings › Apps › SKZ Connect › Battery › Unrestricted*.

### Double inputs

If touch controls are also on and the game supports controllers, turn off touch controls.

### The controller no longer reconnects by itself

Press SYNC once so it remembers this device again. Without Shizuku or root, reconnecting requires the device's
Bluetooth address to be entered (*Settings › Connection*).

### Touch controls cannot be turned on

On Android 13 and later, accessibility services of apps from outside the Play Store are blocked at first: SKZ Connect's
App info › menu (⋮) › “Allow restricted settings”.

### Kodi does not show SKZ Connect's status

After reinstalling SKZ Connect, an old `status.json` may remain in `Documents/SKZ Connect` that the app is not allowed to
change; *Help › Kodi* then points this out. Delete the old file with a file manager, exit SKZ Connect and open it
again. Also allow Kodi access to files.

## Kodi

### Nothing happens on SYNC

In the menu, choose *Run diagnostics*: it shows whether Bluetooth and the virtual controller are ready. Bluetooth must
be turned on under *LibreELEC settings › Bluetooth*, and the adapter needs Bluetooth LE.

### “Paired with another device” message

Press SYNC, and the controller is paired with this device again.

### The connection is rejected immediately (errno 38)

Wi-Fi interferes with Bluetooth, often with a shared antenna as on the Raspberry Pi. Turn off Wi-Fi if you don't need
it (*LibreELEC settings › Connections*), or use a wired network connection.

### Inputs arrive delayed

Reduce the distance to the Bluetooth adapter; a USB extension cable for the dongle helps against interference from
USB 3.

### “SKZ Connect keeps stopping” message

In the menu, choose *Show log* and report the error with the log. On desktop Linux, the service does not start without
root privileges.

### The display stays empty in companion mode

The SKZ Connect app must be running on the same device (on Android, 0.9.1 or later). If Kodi was installed after
SKZ Connect, restart SKZ Connect once. On macOS there is no app, so the display stays empty.

## Portable version

- The [release page](https://github.com/DevCatSKZ/SKZ-Connect/releases/latest) has
  `SKZ-Connect-Portable-<version>.zip` with `SKZ-Connect.exe`, `portable.txt`, `LICENSE` and `THIRD-PARTY-NOTICES.md`.
  Extract it to a folder and start `SKZ-Connect.exe`.
- Settings and log are stored in the `data` subfolder next to the EXE instead of in `%APPDATA%` and `%LOCALAPPDATA%`.
- The drivers [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) and
  [HidHide](https://github.com/nefarius/HidHide/releases) must be installed once on each PC.
- Without the setup's scheduled task, Windows asks for administrator rights when hiding the original controllers.
- An update downloads the new ZIP and replaces the EXE after SKZ Connect exits.

## Diagnostics and bug reports

Please report bugs as an [issue](https://github.com/DevCatSKZ/SKZ-Connect/issues): what happened, which controller,
which system – and ideally with diagnostics.

- **Windows:** *General › Advanced › Export diagnostics* creates a ZIP file with log, settings and system information
  (Windows version, driver versions of ViGEmBus and HidHide, controllers). It contains no pairing keys, but the log
  includes the names and Bluetooth addresses of the controllers. The log alone: notification area › *Open log*.
- **Android:** *Help › Diagnostics › Share* (or *Show log*). Apart from the controllers' Bluetooth addresses, the log
  contains no personal data.
- **Kodi:** menu › *Run diagnostics* and *Show log*; the file is at
  `userdata/addon_data/service.nconnect/nconnect.log`.

Feedback is especially helpful for controllers that have not yet been tested with real hardware: DualShock 4,
DualSense, Xbox via Bluetooth or Wireless Adapter, wired controllers by HORI, PowerA and PDP, and Joy-Con in the
charging grip.
