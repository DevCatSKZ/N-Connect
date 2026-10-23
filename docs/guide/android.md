# SKZ Connect for Android (beta)

[Deutsch](../anleitung/android.md) · **English** · As of version 0.9.1

SKZ Connect for Android connects **Switch 2 controllers** (Pro Controller 2, Joy-Con 2 individually or as a pair,
GameCube controller) to your phone or tablet via Bluetooth LE. Games and emulators see a virtual Xbox controller; for
games without controller support there are touch controls. Problems and solutions:
[Troubleshooting](troubleshooting.md#android).

## Other controllers

The app supports only Switch 2 controllers. They use their own Bluetooth LE protocol, which Android cannot use on its
own. All other controllers are paired directly with the system in Android's Bluetooth settings; SKZ Connect does not see
them.

- **Xbox and PlayStation controllers** work there with Android's own drivers.
- **Older Switch controllers** (Switch 1 Pro Controller and Joy-Con, Pro Controller clones) use Bluetooth Classic.
  Paired directly with Android, they usually provide only buttons and sticks; whether motion controls and vibration
  work depends on the device. The app currently has no separate path for Bluetooth Classic controllers.

## Requirements

- Android 8.0 or later with Bluetooth LE.
- For the virtual Xbox controller, [Shizuku](https://shizuku.rikka.app/) (no root) or root. Without either, only touch
  controls remain.
- On Android 11 and later, Shizuku can be started via wireless debugging over Wi-Fi. On Android 8 to 10, start
  Shizuku once via USB from a PC (adb), as described in the Shizuku app.

## Installation

1. Download and open
   [SKZ-Connect-Android-0.9.1.apk](https://github.com/DevCatSKZ/SKZ-Connect/releases/download/v1.0.22/SKZ-Connect-Android-0.9.1.apk).
   Android asks once whether your browser or file manager may install apps: allow it.
2. The app does not come from the Play Store. If Play Protect warns about an unknown app: “More details” ›
   “Install anyway”.
3. Open SKZ-Connect. The setup assistant guides you through the permissions (nearby devices, notifications, unrestricted
   battery use), choosing the output method, setting it up with a function test, and the first connection. To run it
   again later: *Settings › General › Run setup again*.

Updates: *Settings › General › Check for updates* downloads a new version from GitHub, checks its size and checksum and
starts the installation. Android only asks for the “Install unknown apps” permission at that point.

## Setting up the output method

### Shizuku (no root)

1. Install Shizuku from the Play Store.
2. Turn on Developer options: *Settings › About phone* › tap *Build number* seven times.
3. Connect to a Wi-Fi network and turn on *Wireless debugging* in Developer options.
4. In Shizuku, under “Start via Wireless debugging”, tap *Pairing*, enter the pairing code in the notification, then
   tap *Start*.
5. Back in SKZ Connect: *Allow access*.

After every device restart, tap *Start* once in Shizuku (Wi-Fi required); the pairing is kept. If Shizuku starts
later, SKZ Connect picks it up without restarting the app.

### Root

With Magisk, KernelSU or APatch, choose *Root* in the setup assistant and allow access in the root dialog. Root
persists after a restart and works without Wi-Fi. The root path is untested because no rooted test device was
available.

### Touch controls

For games without controller support, SKZ Connect taps the on-screen buttons; sticks become dragging fingers.

1. In Android's settings under *Accessibility*, turn on the accessibility service *SKZ Connect touch controls*.
   On Android 13 and later it is blocked at first for apps from outside the Play Store: SKZ Connect's App info ›
   menu (⋮) › “Allow restricted settings”.
2. In SKZ Connect, choose touch controls as the method or turn on *Settings › Output › Additional touch controls*.
3. In the game, pull down the notification shade and tap *Edit touch layout* on SKZ Connect's notification.
4. Use *Add button* to create buttons and sticks and drag them onto the game's buttons; a long press removes a button.
   *Save*.

Touch controls have only been partly tested on a device.

## Connecting controllers

- **The first time:** Hold the small SYNC button until the lights run back and forth. SKZ Connect finds the controller
  by itself.
- **After that:** Press a button and the controller reconnects. For this, SKZ Connect needs the device's Bluetooth
  address; with Shizuku or root it reads it itself, otherwise enter it under *Settings › Connection › Bluetooth
  address of this device* (you'll find it under *Settings › About phone*).
- **Joy-Con:** connect both one after the other; they become one controller (*Settings › Controller › Combine Joy-Con
  into one controller*).
- A controller remembers exactly one device. To use it on the Switch 2 again, press SYNC there – and afterwards on the
  phone as well.
- Remembered controllers are listed on the Controllers page, where you can also forget them.

## In games and emulators

- Games see an Xbox controller named “SKZ Connect Player 1” and so on. In emulators, select this controller and map the
  buttons if needed.
- *Settings › Controller*: *Button layout A/B/X/Y* (by position like Xbox or by label like Switch), vibration and
  strength, dead zones for sticks and triggers.
- *Settings › Motion controls › Gyro aiming*: tilting the controller moves the right stick.

## New in 0.9.1

These features are built and covered by unit tests but have not yet run on a device.

- **Per-game profiles:** *Settings › Manage per-game profiles*. Allow “Usage access” once (SKZ Connect only uses it to
  detect which app is in the foreground). *New profile* › choose a game; the profile starts as a copy of the default
  mapping. *Edit button mapping*: for each controller, assign any button to an Xbox button, no function, gyro as right
  stick or a macro. *Active profile* also lets you fix a profile.
- **Gyro for emulators (DSU):** *Settings › Motion controls › Send gyro to emulators (DSU)*. In the emulator, under
  DSU, Cemuhook or “UDP server”, enter the address `127.0.0.1` and the port `26760`; players 1 to 4 take the slots.
  The server can only be reached on this device. Which Android emulators support DSU has not been checked.
- **Tile and autostart:** Add the *SKZ Connect* tile in Quick Settings; one tap starts or stops the connection.
  *Settings › Connection › Start with Android* connects after the device is switched on.
- **Kodi on the same device:** If Kodi is installed, SKZ Connect writes its status to `Documents/SKZ-Connect/status.json`;
  the Kodi add-on 0.4.4 or later displays it. Whether Kodi may read the file on Android 11 and later is unverified.
  *Help › Kodi: DevCatSKZ Repository* sets up the repository (*Set up directly* with Shizuku or root) or downloads it.
  More: [Kodi](kodi.md).
- Battery warning at 15 % and 5 %, licenses under *Help › Licences*.

## Permissions

| Permission | Purpose |
|---|---|
| Nearby devices (Bluetooth) | Find and connect controllers |
| Notifications | Status display, battery warnings |
| Unrestricted battery use | Keep the connection while playing |
| Usage access | Per-game profiles only |
| Internet | Update check and DevCatSKZ Repository only, each on tap |

## Test status

- Tested on a Pixel 10a (Android 17) with version 0.9.0 and Shizuku: connecting, playing, virtual Xbox controller,
  setup assistant.
- Untested: the root path, touch controls in full, other devices (such as Samsung), Android 8 and 9, all new features
  of 0.9.1, the trigger behavior of the GameCube controller.

Feedback as an [issue](https://github.com/DevCatSKZ/SKZ-Connect/issues), ideally with a log: *Help › Diagnostics ›
Share*. Technical details (developer documentation, German): [ANDROID.md](../ANDROID.md).
