<img src="installer/art/N-Connect.png" width="96" align="right" alt="">

# N-Connect

[Deutsch](README.md) · **English**

Use and manage **Nintendo, PlayStation and Xbox controllers** on your PC – in **Windows, Steam,
Xbox/Game Pass games, Epic, emulators** and any other program that supports controllers.
Every controller appears as an **Xbox 360 controller** (or optionally as a **DualShock 4** with
motion controls).

Install once, connect your controller, play. The interface is available in twelve languages –
German, English, Spanish, French, Italian, Portuguese, Dutch, Polish, Russian, Japanese, Chinese
and Korean (follows the Windows language or your choice in the setup).

## Supported controllers

| Controller | Connection | Notes |
|---|---|---|
| **Switch 2 Pro Controller** | Bluetooth (SYNC) or **USB cable** (up to ~500 Hz) | GL/GR, C button, gyro, HD rumble |
| **Joy-Con 2 (L/R)** | Bluetooth (SYNC) | as a pair or individually, **mouse mode** (place on the desk), Charging Grip with GL/GR |
| **GameCube controller (Switch 2)** | Bluetooth (SYNC) or USB cable | analogue triggers |
| **Switch Pro Controller** (Switch 1) | Bluetooth (SYNC, N-Connect pairs it itself) or USB | gyro, rumble, **amiibo reading** |
| **Joy-Con (L/R)** (Switch 1) | Bluetooth (SYNC, N-Connect pairs it itself) | pair or single, **amiibo**, **Ring-Con**, **IR camera** (right Joy-Con) |
| **NES, SNES, N64, SEGA Mega Drive** (Nintendo Switch Online) | Bluetooth (SYNC, N-Connect pairs it itself) | own layout, N64 C buttons = right stick |
| **Wii Remote** (incl. Plus) | Bluetooth (SYNC, N-Connect pairs it itself) | with **Nunchuk** or **Classic Controller** |
| **Wii U Pro Controller** | Bluetooth (SYNC, N-Connect pairs it itself) | both sticks, battery display |
| **Wired pads by HORI, PowerA, PDP** (for Switch) | USB cable | like Pro Controller, without gyro/rumble (not yet verified on real hardware) |
| **Clones in Switch mode** (e.g. 8BitDo, "Lic Pro Controller") | like Switch Pro Controller | as far as the clone speaks the protocol |
| **Sony DualShock 4** | USB cable or Bluetooth (pair via Windows) | touchpad click, gyro, light bar follows the game |
| **Sony DualSense / DualSense Edge** | USB cable or Bluetooth (pair via Windows) | touchpad, gyro, player LEDs, mic button, Edge back buttons |
| **Xbox controllers** (360, One, Series, Elite etc.) | USB, Bluetooth or Xbox Wireless Adapter | managed **natively** – battery, own mapping for special actions, no duplicate controller |

Up to 8 controllers at once (players 1–8). Tip for many controllers: a capable Bluetooth adapter
(e.g. Intel AX200/AX210 or a Realtek Bluetooth 5.3 stick) – simple sticks often only handle 2–3 controllers.

## Installation

1. **Download `N-Connect-Setup-….exe`** (GitHub → *Releases* or *Actions* → latest run → *Artifacts*).
2. Run the setup. You can choose the language and the colour scheme (dark by default). It automatically installs:
   - the program (runs unobtrusively in the notification area),
   - the signed **ViGEmBus** driver for the virtual controller (if not already present),
   - **HidHide** (if not already present; prevents Steam and games from seeing Switch 1, NSO and USB controllers twice –
     restart once afterwards).
3. N-Connect then **starts automatically with Windows** in the background (can be disabled under *General → Start with Windows*).
4. A short guide appears on first start.

Requirements: Windows 10 (2004) or Windows 11, 64-bit, Bluetooth 4.0+ (Bluetooth LE for Switch 2 controllers).

**Portable:** Alternatively, the release offers `N-Connect-Portable-….zip` – just unpack and run `N-Connect.exe`,
no installation. The included `portable.txt` makes the app store settings and the log in a `data` subfolder next
to the EXE instead of %APPDATA%. **Note:** ViGEmBus and HidHide still have to be installed once per PC (via the
setup or from the vendors' pages) – otherwise the portable EXE cannot create virtual controllers.

## Connecting controllers

**Switch 2 (Pro Controller, Joy-Con 2, GameCube):** press the **SYNC button** briefly. After a few seconds the
controller rumbles – done. From then on, **any button press** reconnects it.
Pro Controller and GameCube controller also work simply via **USB cable**.

> ⚠️ Do **not** pair Switch 2 controllers via *Settings → Bluetooth → Add device* – they use Nintendo's own
> procedure; the Windows pairing interferes with the connection.
> Note: after connecting to the PC, the controller must be paired once again at the Switch 2 (SYNC at the console).

**Switch 1, Nintendo Switch Online and Wii controllers:** just press the **SYNC button** (Joy-Con: on the rail,
Wii Remote: red button in the battery compartment, Wii U Pro: underside). N-Connect pairs the controller
**itself** with Windows – no detour via the Windows Bluetooth settings. Afterwards a button press is enough.
The background search only runs while nobody is playing; to search deliberately: *General → Pair controller …*
or the **"Search for controllers …"** button directly on the Controllers page
(can be disabled: *Automatically pair new controllers*).

**DualShock 4 / DualSense:** pair once via the Windows Bluetooth settings (or connect via USB cable) –
N-Connect detects them on its own, shows battery and gyro, and hides them from games so only the
virtual controller counts.

**Xbox controllers:** just connect (USB, Bluetooth or Xbox Wireless Adapter). It appears in the overview
with battery and XInput slot; games use it directly – deliberately **no** second (virtual) controller
is created.

## What the program can do

- **Overview** with live graphics of every controller (shape and button positions from product photos, original
  colours, pressed buttons light up), battery, connection, serial number, firmware. Two columns in a wide window,
  all cards equally tall.
- **Settings right on the controller card** (expand "Settings"): buttons, fine-tuning, gyro, Joy-Con, extras,
  details – each only for that controller.
- **Assign a button by pressing it:** click the keyboard icon behind any button and press the key you want.
- **Windows 11-style interface:** dark (default), light or same as Windows; accent colour in brand blue, Mica title bar.
- Buttons per controller: **Disconnect**, **Rumble** (which controller is which player?), **Calibrate gyro**,
  Joy-Con **split/join**, **upright/sideways**, **read amiibo**, **Ring-Con**, **IR camera**,
  **"Shown twice? Hide"** (HidHide).
- **Prepared for Steam:** original controllers (Nintendo **and** Sony) are automatically hidden from Steam and
  games (HidHide) – Steam only sees the virtual Xbox controller. **Appears as** selectable per controller
  (Xbox 360 or DualShock 4 with gyro), default Xbox 360.
- **Xbox and PlayStation controllers are managed too:** Xbox (360/One/Series/Elite, USB, Bluetooth or the
  Microsoft adapter – via XInput) appears natively in the overview with battery, slot and its own button mapping
  for special actions, without a duplicate virtual controller. DualShock 4 and DualSense (incl. Edge) are managed
  like Nintendo controllers: own graphics and labels (△ ○ ✕ □, L1–L3/R1–R3, Share/Options, PS button, touchpad),
  analogue triggers, gyro, rumble and light bar/player LEDs – optionally as a virtual Xbox 360 or DualShock 4
  controller for games.
- **Joy-Con in the charging grip via USB** (Switch 1) are recognised.
- **Gyro extras like JoyShockMapper:** flick stick (the right stick turns the camera instantly in its direction,
  adjustable per game via "test turn"), gyro acceleration, "hold gyro" button (ratcheting).
- **Feedback from the game:** player LEDs show the Xbox slot assigned by Windows; with DualShock 4 the game's
  light bar drives the HOME LED (Switch 1 Pro, Joy-Con R) and is shown on the card.
- **Player order:** bar above the cards – use ‹ › to set which controller is player 1, 2 …
  (player 1 = first controller for Windows, Steam and games; lights follow, remembered per controller).
  When a controller disconnects, the others move up automatically. Also via the card title (rename there too,
  e.g. "Lena's Joy-Con").
- **Stick calibration** (against drift): guided measurement of centre and edge, with roundness indicator – stored
  only in N-Connect, the controller stays unchanged.
- **Gyro assistant:** set up aiming by motion in three steps, with live preview.
- **Joy-Con like on the Switch:** two Joy-Con automatically become one controller; holding one Joy-Con sideways and
  pressing SL or SR makes it its own player, pressing L + R at the same time rejoins them.
  The choice is remembered per Joy-Con.
- **Joy-Con 2 mouse mode:** place the Joy-Con on its rail edge → mouse (R/L = left click, ZR/ZL = right click,
  stick = scrolling).
- **Button mapping per controller type:** every button to gamepad buttons, **keyboard hotkeys**, **mouse buttons**,
  **gyro mouse**, **gyro as right stick**, **turbo/auto-fire**, **macros** (key sequences) or ready-made templates
  (screenshot, recording, Xbox Game Bar, volume …).
- **Shift layer:** hold one button → other buttons get a second mapping.
- **Profiles per game:** automatically active while the game is in the foreground; export/import as a file.
- **Aiming by motion:** gyro as right stick (always, while aiming with ZL, or via a button) – for games without a mouse.
- **Fine-tuning:** stick deadzone (also per controller type), stick curve, trigger threshold, turbo speed.
- **Emulators:** motion data via **Cemuhook/DSU** (port 26760), e.g. for Cemu, Yuzu successors, Dolphin.
- **Battery icon** in the notification area, **warning** at low battery, **automatic disconnect** on inactivity
  (adjustable). Switch 2 controllers only report voltage; N-Connect converts it smoothed into percent – also while
  charging via cable (the voltage rise during charging is measured per controller and compensated).
- **Pairing data from the Switch (Switch 1):** N-Connect reads pairing data saved to the SD card with the
  *Bluepick RCM* payload (`switchroot/joycon_mac.ini`) – the SD card in a card reader or the connected Switch is
  detected automatically (*General → Import pairing data from the Switch*).
- **Transfer pairing data to another PC:** export as a `.ncpair` file (optionally with a password, keys only on
  explicit request) and open it on the second PC. Bluetooth adapters and Windows pairings are not changed.
- **Notification-area menu:** per player rumble, player slot, disconnect; disconnect all controllers; output mode,
  profile and more.
- **One-click update:** when a new version appears on GitHub, N-Connect downloads the installer on click
  (verified), installs it and restarts.
- **Keep HidHide up to date:** if a new HidHide version exists, N-Connect offers it – after confirmation the signed
  setup is downloaded from the vendor, verified and started; afterwards N-Connect re-hides the controllers itself
  (can be disabled under *General → Advanced*).

All settings are optional and take effect immediately (click the icon in the notification area; the sections on
the left: *Controllers, Button mapping, Sticks & rumble, Gyro & mouse, Joy-Con & Wii, General*).

## Troubleshooting

| Problem | Solution |
|---|---|
| Switch 2 controller won't connect | Press SYNC briefly (don't hold). If it is listed in the Windows Bluetooth settings → **remove** it there. Put a nearby Switch 2 into sleep mode. |
| Switch 1/NSO/Wii controller won't pair | Open *General → Pair controller …* and press SYNC while it searches (the background search pauses while someone is playing). Error codes are in the log ("Pairing …"). After a failure the background search waits 90 s before retrying. |
| Joy-Con suddenly became its own player (buttons/stick rotated) | It was split from the pair (hold sideways + SL/SR). Pressing L on the left and R on the right Joy-Con at the same time rejoins them. |
| Steam/game sees a controller twice | "General → Hide original controllers" (HidHide) must be on; confirm the Windows prompt and restart Steam once. Individually: card → Extras → "Shown twice?". |
| Battery reading off while charging | Unplug and replug the cable once – N-Connect then re-measures the voltage rise. Values are logged every 30 s ("Battery …"). |
| "ViGEmBus driver missing" | Run the setup again or install ViGEmBus: <https://github.com/nefarius/ViGEmBus/releases> |
| Something else | Right-click the icon → **Open log** and attach its content to a bug report. |

Log: `%LOCALAPPDATA%\N-Connect\bridge.log` · Settings: `%APPDATA%\N-Connect\settings.json` ·
Battery readings: `%LOCALAPPDATA%\N-Connect\battery.json` (folders of the previous version `Switch2ProBridge` are migrated once)

## How it works (technical)

Detailed developer documentation (all features and their behaviour, architecture, controller protocols,
porting to other platforms): **[docs/](docs/README.md)**.

The program talks to the controllers in **user mode** directly (Bluetooth LE via WinRT, HID, WinUSB) and feeds
the inputs to **ViGEmBus**, a signed kernel driver that provides a virtual Xbox 360 or DualShock 4 controller.
No custom kernel driver is needed.

```
src/Switch2Pro.Protocol   Protocols (platform-independent, with tests): Switch 2 (BLE/USB), Switch 1 (HID, NFC,
                          IR camera, Ring-Con), Wii, mapping, macros, DSU, DS4 report
src/Switch2Pro.Bridge     Windows app: connections, ViGEm output, notification area, windows, translation
tests/                    xUnit tests
installer/                Inno Setup script (Setup.exe with ViGEmBus and HidHide)
```

Build it yourself: .NET 8 SDK, then `dotnet test tests/Switch2Pro.Protocol.Tests` and
`dotnet publish src/Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out/publish/win-x64`.
The installer is built by the workflow `.github/workflows/switch2-pro-windows.yml`.
Build the installer locally: Inno Setup 6, then `ISCC.exe installer\N-Connect.iss` (output in `out\`).
Check helpers: `N-Connect.exe --render <folder>` (all controller graphics as PNG), `--render-ui <folder>` (all pages
and cards, light/dark, `--lang=xx` for the language; `--wide` for wide windows), `--render-brand <folder>` (logo,
icon, installer images), `--demo` / `--demo-all` / `--demo-retro` (simulated controllers).

### Sources and thanks

Protocol information (used as documentation only, own implementation):
[ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research),
SDL (`SDL_hidapi_switch2.c`, `SDL_hidapi_switch.c`, zlib), dekuNukem/Nintendo_Switch_Reverse_Engineering,
Linux `hid-nintendo` and `hid-wiimote`, WiiBrew, the emulators yuzu/Citron (NFC, IR camera, Ring-Con) and Dolphin
(Wii pairing), Switch2Connect. Start sequence and rumble format over Bluetooth: NS2Pro-Bridge-Windows (MIT).
Virtual controller: [ViGEmBus](https://github.com/nefarius/ViGEmBus), [HidHide](https://github.com/nefarius/HidHide) by Nefarius.

Developed by **devcatskz**. Unofficial project, not affiliated with Nintendo. "Nintendo", "Switch", "Wii", "amiibo" are trademarks of Nintendo;
"SEGA" and "Mega Drive" are trademarks of SEGA.

## License

[MIT](LICENSE) © devcatskz
