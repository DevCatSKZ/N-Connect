# SKZ Connect for Kodi

[Deutsch](../anleitung/kodi.md) · **English** · As of add-on `service.skzconnect` 0.4.4

On LibreELEC, the add-on starts the `skzconnect` service together with Kodi. It connects Switch 2 controllers via
Bluetooth LE and creates a virtual Xbox 360 controller for each player, which Kodi, RetroArch and games recognize
without any setup. It can pair other Bluetooth controllers automatically. On Kodi for Windows and Android, the add-on
only shows the status of the SKZ Connect app there (companion mode). Problems and solutions:
[Troubleshooting](troubleshooting.md#kodi).

## Systems and test status

| System | Mode | Status |
|---|---|---|
| LibreELEC on Raspberry Pi 4 | standalone | tested with LibreELEC 12.2.1 (Kodi 21.3), Pro Controller 2 and Joy-Con 2; the changes from 0.4.4 not yet |
| LibreELEC on other devices (Raspberry Pi 2, 3 and 5, PCs, 64-bit ARM boxes) | standalone | untested |
| Batocera | standalone | untested |
| Desktop Linux (Kodi as a regular user) | – | The service needs root privileges (Bluetooth, uinput); it does not start as a regular user. |
| Kodi on Windows | companion mode with SKZ Connect for Windows | untested |
| Kodi on Android | companion mode with SKZ Connect for Android 0.9.1 or later | untested |
| Kodi on macOS | notice only, there is no SKZ Connect app | – |

Also untested: vibration in games and pairing Xbox, PlayStation, Switch 1 and Wii controllers. Feedback as an
[issue](https://github.com/DevCatSKZ/SKZ-Connect/issues) helps.

## Requirements

- Bluetooth with Bluetooth LE (4.0 or later). The Raspberry Pi 4 and 5 have it built in; otherwise a USB dongle is
  enough (Bluetooth 5.0 or later recommended).
- Bluetooth must be turned on: *LibreELEC settings › Bluetooth*.
- The add-on contains the program for x86_64, ARM64 and ARM (32-bit) and picks the right one at startup.

## Installation

### Via the DevCatSKZ Repository (with updates)

1. In Kodi, turn on *Settings › System › Add-ons › Unknown sources*.
2. Get `repository.devcatskz.zip` onto the device in one of these ways:
   - download [repository.devcatskz.zip](https://github.com/DevCatSKZ/kodi-repo/raw/main/repository.devcatskz.zip) and
     copy it to the device via USB stick or network share (on LibreELEC, e.g. Samba),
   - in Kodi, under *Settings › File manager › Add source*, enter the address `https://devcatskz.github.io/kodi-repo/`;
     the ZIP is in the `repository.devcatskz` folder there,
   - from SKZ Connect: on Windows *General › Kodi › Download repository*, on Android *Help › Kodi: DevCatSKZ
     Repository › Download*.
3. *Add-ons* › package icon › *Install from zip file* › select the ZIP.
4. *Install from repository* › *DevCatSKZ Repository* › *SKZ Connect* › *Install*.

If Kodi runs on the same device as SKZ Connect, *Set up in Kodi* (Windows, *General › Kodi*) or *Set up directly*
(Android, with Shizuku or root) places the repository directly in Kodi. Then in Kodi go to *Add-ons › My add-ons ›
Add-on repository › DevCatSKZ Repository › Enable* and continue with step 4.

Kodi keeps add-ons from the repository up to date by itself as soon as a new version is released.

### Single ZIP file (without updates)

The [release](https://github.com/DevCatSKZ/SKZ-Connect/releases/latest) has the ZIP files with version numbers, the
[repository](https://github.com/DevCatSKZ/kodi-repo/tree/main/downloads) has them under fixed names. Install them via
*Install from zip file*; Kodi does not update such installations.

| File | For |
|---|---|
| `SKZ-Connect-Kodi-LibreELEC-…-alle-Geraete.zip` | all devices (x86_64, ARM64, ARM), recommended |
| `SKZ-Connect-Kodi-LibreELEC-…-RPi4-RPi5-arm64.zip` | Raspberry Pi 4 and 5, other 64-bit ARM devices |
| `SKZ-Connect-Kodi-LibreELEC-…-RPi2-RPi3-arm.zip` | Raspberry Pi 2 and 3 (32-bit) |
| `SKZ-Connect-Kodi-LibreELEC-…-PC-x86_64.zip` | PCs with LibreELEC Generic |

The *SKZ Connect Bundle Installer* add-on (`script.skzconnect.bundle`, in the repository) installs SKZ Connect and the
widget add-on without internet and adds a menu item with widget to the Aeon Nox Silvo skin; restart Kodi afterwards.

## Connecting controllers

1. Press the small round SYNC button until the lights run – on the Pro Controller 2 at the top next to the USB-C port,
   on the Joy-Con 2 on the flat inner side between SL and SR, on the GameCube controller on top.
2. After a few seconds, “Player 1: … connected” appears. SKZ Connect pairs the controller with this device in the
   process.

- **Reconnecting:** press any button, such as HOME. This requires *Settings › Connection › Search for controllers
  continuously* to be on (default); otherwise choose *Search for controllers now* in the menu.
- **Joy-Con:** A left and a right Joy-Con become one controller. For two players, turn off *Settings › Controller ›
  Combine two Joy-Con into one controller*.
- **Other controllers** (untested): If *Settings › Connection › Pair other controllers automatically* is on (default),
  pairing mode is enough – Xbox: hold the pairing button on top until the logo blinks quickly; PlayStation 4: hold
  SHARE + PS; PlayStation 5: hold CREATE + PS; Switch 1: SYNC; Wii: red SYNC button; 8BitDo: as described in its
  manual. The Linux drivers handle the input.
- **Back to the Switch 2:** A controller remembers only one device. On the Switch 2, attach the controller or press
  SYNC under *Controllers › Change Grip/Order*; for Kodi, press SYNC again afterwards.

## Usage

- **Menu:** *Add-ons › Program add-ons › SKZ Connect* (the C button opens it as well). It shows players with battery,
  other gamepads and known controllers that are currently not connected; plus *Disconnect*, *Search for controllers
  now*, *Disconnect all controllers*, the guide, diagnostics, log and settings.
- **Home widget:** depending on the skin, under *Skin settings › Home › Widgets › Add-ons › SKZ Connect*.
- **Controlling Kodi:** move the D-pad or stick, the bottom button selects, the right button goes back (mapping by
  position). Custom mapping: *Kodi settings › System › Input › Configure attached controllers*.
- **Extra buttons:** Capture takes a screenshot, C opens the SKZ Connect menu; change this under *Settings › Kodi ›
  Controller buttons in Kodi*, also for GL/GR on the Pro Controller 2.
- **Player slots:** Each controller gets the same slot again the next time it connects.
- **Battery:** warning at 20 % and 10 %.
- **Gyro for emulators (DSU):** *Settings › Emulators (motion control)* – port 26760; in the emulator, add `127.0.0.1`
  as the “DSU” or “Cemuhook” source. *Also for other devices on the home network* opens the port to the network.
- Further settings: button layout, stick dead zone, gyro stick, rumble and strength, player LEDs, disconnect when
  unused, Bluetooth adapter, notifications.

## Companion mode: Kodi on Windows, Android and macOS

Here the SKZ Connect app connects the controllers, not the add-on. The add-on only shows in its menu and home widget what
the app reports. Disconnecting, pairing and diagnostics are not available there; the settings for controllers,
emulators, vibration, connection and Kodi are locked and carry a notice. Settings are changed in the app.

- **Windows:** Install and start [SKZ Connect for Windows](windows.md). Kodi sees the controllers as Xbox controllers;
  the app writes its status to Kodi's data folder (also with Kodi from the Microsoft Store).
- **Android:** Install and start [SKZ Connect for Android](android.md) 0.9.1 or later. The app writes its status to
  `Documents/SKZ Connect`, and the add-on reads it from there. If Kodi shows nothing, allow Kodi access to files in the
  Android settings; whether that is enough on Android 11 and later is unverified.
- **macOS:** There is no SKZ Connect app; the add-on only shows a notice.

## Diagnostics

In the menu, *Run diagnostics* checks whether Bluetooth, the adapter with Bluetooth LE and the virtual controller
(uinput) are ready. *Show log* shows the latest entries; the file is at
`userdata/addon_data/service.skzconnect/skzconnect.log`. Both help when reporting a bug as an
[issue](https://github.com/DevCatSKZ/SKZ-Connect/issues).

Technical details (developer documentation, German): [LINUX-KODI.md](../LINUX-KODI.md).
