# SKZ Connect for Windows

[Deutsch](../anleitung/windows.md) · **English** · As of version 1.0.22

SKZ Connect connects Nintendo, PlayStation and Xbox controllers to Windows and passes them on to games, Steam and
emulators as a virtual Xbox 360 or DualShock 4 controller. Problems and solutions:
[Troubleshooting](troubleshooting.md).

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11; 64-bit (x64).
- For Switch 2 controllers, a Bluetooth adapter with Bluetooth LE (4.0 or later). Pro Controller 2 and GameCube
  controller also work via USB cable. Switch 1, Wii and PlayStation controllers use classic Bluetooth.
- No .NET installation is needed; the app ships with its own runtime.

## Installation

1. Download `SKZ-Connect-Setup-<version>.exe` from the
   [release page](https://github.com/DevCatSKZ/SKZ-Connect/releases/latest) and run it.
2. Setup and app are not code-signed, so Windows SmartScreen shows “Windows protected your PC”: “More info” ›
   “Run anyway”.
3. If the drivers are missing, setup installs their official, signed installers:
   [ViGEmBus](https://github.com/nefarius/ViGEmBus) 1.22.0 creates the virtual controllers,
   [HidHide](https://github.com/nefarius/HidHide) 1.5.230.0 hides the original controllers from games. After installing
   HidHide, setup asks for a restart.
4. SKZ Connect starts in the notification area of the taskbar (system tray). Click the icon to open the window.

Setup also creates the scheduled task “SKZ Connect HidHide” so that hiding works without a prompt (see
[Duplicate controllers](#avoiding-duplicate-controllers)). Uninstalling removes the task, the autostart entry and the
HidHide permission; ViGEmBus and HidHide themselves stay installed because other programs may use them.

Without installing: [portable version](troubleshooting.md#portable-version).

## Connecting controllers

| Controller | How to |
|---|---|
| Switch 2: Pro Controller 2, Joy-Con 2, GameCube controller | Briefly press the small SYNC button. After a few seconds the controller vibrates. Pro Controller 2 and GameCube controller also work via USB cable. |
| Switch 1: Pro Controller, Joy-Con, NES, SNES, N64, Mega Drive | Press the SYNC button until the lights run. SKZ Connect pairs the controller itself. |
| Wii Remote | Open the battery compartment and press the red SYNC button. |
| Wii U Pro Controller | Press the SYNC button on the bottom. |
| DualShock 4, DualSense | Hold PS + Share or PS + Create until the light bar blinks quickly; SKZ Connect pairs it itself. Alternatively via USB cable or the Windows Bluetooth settings. |
| Xbox | As usual via USB, Bluetooth or the Xbox Wireless Adapter. Over Bluetooth, “Pair controller …” can also pair the controller (hold the pairing button on top). |
| Wired controllers by HORI, PowerA, PDP | Plug in the USB cable. |

After that, a single button press is enough next time. Up to 8 controllers work at the same time.

- Do not pair Switch 2 controllers via “Bluetooth › Add device”. Such a Windows pairing disturbs the connection;
  SKZ Connect removes it itself.
- A Switch 2 controller remembers exactly one device. To use it on the Switch 2 again, pair it there once with SYNC;
  afterwards press SYNC on the PC again.
- SKZ Connect looks for new controllers in the background, but not while someone is playing. For a targeted search, use
  “Search for controllers …” on the Controllers page or “Pair controller …” in the notification area menu (for one
  minute).
- PlayStation and Xbox controllers via Bluetooth, Joy-Con in the charging grip and wired controllers have not yet been
  tested with real hardware. Please send feedback as an [issue](https://github.com/DevCatSKZ/SKZ-Connect/issues).

## Window and notification area

- **Controllers page:** one card per player with live graphic, battery and connection. Buttons: *Vibrate* (which one
  is which?), *Disconnect* and *Settings* with the tabs Buttons, Fine-tuning, Gyro, Joy-Con, Extras, Details and Test.
  View *Large* or *Compact*.
- **Player order:** With two or more players, the bar above the cards swaps their slots. Player 1 is the first
  controller for Windows, Steam and games. Clicking the card title lets you choose the slot or rename the controller.
- **Notification area:** Right-click opens the menu with *Vibrate*, *Player slot*, *Appears as* and *Disconnect* for
  each player, plus *Output as*, the profile, *Quick guide* and *Open log*.

## Output: Xbox 360 or DualShock 4

- *General › Output › Controller appears as*: **Xbox 360** (default, works with almost all games and Steam) or
  **DualShock 4** (with motion data for Steam and emulators). Per controller: card › Settings › Buttons ›
  *Appears as*.
- *A/B/X/Y layout*: Xbox (by position, the bottom button is A) or Nintendo (by label).
- Xbox controllers do not get a virtual controller; games see them directly. SKZ Connect shows battery and player slot
  and adds keyboard, mouse, macro and gyro mappings.

## Buttons and profiles

- *Button layout* page: Every button can trigger a controller button, a keyboard shortcut, a mouse button, turbo or a
  macro, including ready-made presets such as screenshot, Xbox Game Bar, volume or microphone mute. The keyboard icon
  next to a button assigns it by pressing a key.
- **Shift layer:** While a button set to “Shift layer” is held, the other buttons have a second mapping.
- **Per-game profiles:** A profile applies automatically while one of its programs is in the foreground. Profiles can
  be exported and imported as files.

## Sticks, triggers, vibration

- *Sticks & vibration* page: dead zone, response curve, trigger thresholds (GameCube), vibration and strength, turbo
  speed; separate values per controller type are possible.
- **Calibrate sticks:** card › Settings › Fine-tuning › *Calibrate …*. It measures center and edge; the values are
  stored only in SKZ Connect, the controller itself stays unchanged.
- **Test page:** the card's *Test* tab – sticks with roundness display, triggers, motion sensors, buttons, report rate
  and rumble test.

## Motion controls

- *Gyro & mouse* page: gyro as right stick (always, only while aiming, or via a button), gyro mouse, gyro acceleration
  and flick stick. Set it up with a preview: card › Settings › Gyro › *Set up …*.
- **Gyro for emulators (DSU):** *General › Advanced* (on by default). In the emulator, such as Cemu or Dolphin, enter
  `127.0.0.1` and port `26760` as the DSU/Cemuhook source; players 1 to 4 take the slots. A change takes effect after
  restarting SKZ-Connect. If another program already uses the port (such as DS4Windows), SKZ Connect runs without DSU.

## Joy-Con and Wii

- A left and a right Joy-Con automatically become one controller. To use one on its own: hold it sideways and press SL
  or SR. To rejoin: press L on the left and R on the right Joy-Con at the same time.
- Single Joy-Con held sideways or upright (switchable per Joy-Con).
- **Joy-Con 2 as a mouse:** lay it on its rail edge and slide it like a mouse.
- **Switch 1:** read amiibo (Pro Controller, right Joy-Con; saved as `.bin`), Ring-Con and IR camera (right Joy-Con),
  each under card › Settings › Extras.
- **Wii:** Nunchuk, Classic Controller and MotionPlus are detected; the pointer can control the mouse (sensor bar
  required).
- **Pairing data** (*Joy-Con & Wii* page): take over pairings from a Switch via Bluepick_RCM/hekate or export them for
  another PC, optionally with a password.

## Avoiding duplicate controllers

Steam and many games support Switch 1, NSO and USB controllers, PlayStation controllers and wired pads themselves and
would otherwise see them twice.

- *General › Output › Hide original controllers* (on by default) hides them with HidHide. With the setup version this
  works without a prompt via the scheduled task; in the portable version Windows asks once for administrator rights.
  If Steam is already running, restart Steam once.
- To do this later for one controller: card › Settings › Extras › *Shown twice?*.
- Xbox controllers and virtual controllers are never hidden.
- *General › Advanced › Keep HidHide up to date* reports new HidHide versions; it only installs one after you confirm.

## Desktop mode and widget

- **Desktop mode** (*General*, off by default): When no game is running, the controller controls Windows – left stick
  mouse, right stick scrolling, A click, B right-click, X task view, Y on-screen keyboard, D-pad arrow keys, LB/RB
  back/forward, Start Enter, Back Esc, HOME Start menu, hold RT for a precise pointer. Hold Back + Start for one
  second to pause it; it is off in full-screen games and in programs with their own profile.
- **Desktop widget** (*General*, off by default): small card on the desktop with controllers, battery and connection.
  Right-click for options, double-click opens SKZ-Connect.

## Kodi on this PC

*General › Kodi › Set up the DevCatSKZ Repository in Kodi* places the repository directly in Kodi. Then in Kodi:
Add-ons › My add-ons › Add-on repository › DevCatSKZ Repository › Enable, then install SKZ Connect from it.
Kodi sees the controllers as Xbox controllers even without the add-on; the add-on additionally shows status and widget
(companion mode). *Download repository* saves the ZIP for another device. More: [Kodi](kodi.md).

## App and files

- **Updates:** At startup, SKZ Connect checks GitHub for a new version (can be turned off under *General › Advanced ›
  Check for updates*). After you confirm, it downloads the update from GitHub only, checks its size and – if GitHub
  provides it – the SHA-256 checksum, and installs it.
- **Appearance and language:** Dark, Light or Like Windows, five color schemes, 12 languages (“Automatic (like
  Windows)” follows the Windows display language).
- **Start with Windows** (*General › Connecting*, on after the first start).
- **Files:** settings `%APPDATA%\SKZ-Connect\settings.json`, log `%LOCALAPPDATA%\SKZ-Connect\bridge.log`;
  in the portable version in the `data` folder next to the EXE.
- **Third-party licences:** *General › Advanced* opens `THIRD-PARTY-NOTICES.md` from the program folder.
- **Drivers:** ViGEmBus has been archived since 2023 and no longer receives fixes; existing installations keep
  working. The startup log and the diagnostics list the installed versions of ViGEmBus and HidHide.

All features with their exact values (developer documentation, German): [FUNKTIONEN.md](../FUNKTIONEN.md).
