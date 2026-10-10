<div align="center">

<img src="installer/art/N-Connect.png" width="112" alt="N-Connect logo">

# N-Connect

**Nintendo, PlayStation and Xbox controllers on your PC – just connect and play.**

[![Download](https://img.shields.io/github/v/release/DevCatSKZ/N-Connect?label=Download&style=for-the-badge&color=0078D4)](https://github.com/DevCatSKZ/N-Connect/releases/latest)

![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square)
![12 languages](https://img.shields.io/badge/languages-12-0078D4?style=flat-square)
[![License: MIT](https://img.shields.io/badge/license-MIT-0078D4?style=flat-square)](LICENSE)
![Free](https://img.shields.io/badge/free-no%20ads-0078D4?style=flat-square)

[Deutsch](README.md) · **English** · [Website](https://devcatskz.github.io/N-Connect/)

<img src="https://devcatskz.github.io/N-Connect/screenshots/uebersicht.png" width="820" alt="N-Connect: overview with connected controllers, battery level and live button display">

</div>

## What is N-Connect?

Many Windows games only understand Xbox controllers. **N-Connect translates your controller so every game
recognises it** – whether it's a Switch 2, Switch, Wii, PlayStation or Xbox controller. It works in Steam, Xbox/Game
Pass, Epic, emulators and every other game with controller support.

Install once, connect your controller, play. N-Connect runs quietly in the background.

## Get started in 3 steps

| | |
|:---:|---|
| **1** | **Download and install** – get [`N-Connect-Setup` from the release page](https://github.com/DevCatSKZ/N-Connect/releases/latest) and run it. Everything it needs is installed along with it. |
| **2** | **Connect your controller** – usually a press of the **SYNC button** is all it takes (see below). |
| **3** | **Play** – the controller works in your games right away. |

> [!TIP]
> **Windows shows "Windows protected your PC"?** This happens with new programs that don't have an expensive
> certificate. Click **More info → Run anyway**. N-Connect is open source – the complete source code is in this
> repository.

**Requirements:** Windows 10 or 11 (64-bit) and Bluetooth – or a USB cable.

## Connecting controllers

| Controller | How to |
|---|---|
| **Switch 2** – Pro Controller, Joy-Con 2, GameCube | Briefly press the **SYNC button**. The controller vibrates once it's connected. Pro Controller and GameCube also work with a **USB cable**. |
| **Switch** – Pro Controller, Joy-Con, NES, SNES, N64, Genesis | Press the **SYNC button** until the lights run. N-Connect pairs the controller by itself. |
| **Wii Remote, Wii U Pro Controller** | Press the **red SYNC button** (Wii Remote: in the battery compartment, Wii U Pro: on the bottom). |
| **PlayStation** – DualShock 4, DualSense | Pair once in the **Windows Bluetooth settings** or connect with a **USB cable**. |
| **Xbox** | Just connect it – via USB, Bluetooth or the Xbox Wireless Adapter. |

Next time, **pressing any button** is enough to connect.

> [!IMPORTANT]
> **Don't add Switch 2 controllers in the Windows Bluetooth settings** – that disturbs the connection. Just press
> the SYNC button. To use the controller on your Switch 2 again afterwards, pair it there once more.

## What N-Connect does

- 🎮 **Works in every game** – your controller appears as an Xbox controller (or as a PlayStation controller with
  motion controls).
- 👥 **Up to 8 controllers at once** – you decide who is player 1, 2, 3 …
- 🔋 **Battery level at a glance** – with a warning before it runs out.
- ⌨️ **Remap any button** – to keyboard, mouse, turbo or whole button sequences. Profiles per game.
- 🎯 **Aim by moving** – the motion sensors control the camera or the mouse, ideal for shooters.
- 🕹️ **Joy-Con like on the Switch** – as a pair or single, sideways or upright.
- 🩹 **Fix stick drift** – simply re-measure the sticks.
- 🧪 **Test your controller** – check sticks, triggers, motion sensors and buttons live, e.g. when buying used.
- 🖱️ **Desktop mode** (opt-in) – when no game is running, the controller drives mouse and keyboard. Ideal for a PC
  on the TV.
- 🪟 **Desktop widget** (opt-in) – a small card on the desktop with all controllers, battery and connection,
  in your chosen colour scheme.
- 🔄 **One-click updates** – N-Connect tells you when a new version is out.
- 🌍 **12 languages** and a **Windows 11 look** – light, dark and four more colour schemes.

<details>
<summary><b>All features in detail</b></summary>

### Overview
- Live graphic of every controller – pressed buttons light up, sticks move along.
- Status bar: connected controllers, Bluetooth, lowest battery, how games see the controllers.
- **Large** or **Compact** view (up to four controllers side by side).
- Buttons per controller: **Vibrate** (which one is which?), **Disconnect**, **Settings** for just this controller.
- Rename controllers, e.g. "Lena's Joy-Con".

### Buttons and profiles
- Assign any button to controller buttons, **keyboard shortcuts**, **mouse buttons**, **turbo** or **macros** – or
  to ready-made actions like screenshot, recording, Xbox Game Bar or volume.
- Assign by pressing: click the keyboard icon next to a button and press the key you want.
- **Shift layer:** while one button is held, the other buttons get a second function.
- **Profiles per game** switch on automatically when the game is in the foreground – and can be shared as a file.

### Motion and sticks
- **Gyro as right stick** – always, only while aiming, or by button. Guided gyro assistant with preview.
- **Gyro as mouse**, flick stick and more extras for shooters.
- **Calibrate sticks** against drift (stored in N-Connect only, the controller stays unchanged).
- Deadzone, sensitivity curve, trigger thresholds and rumble strength – globally or per controller.
- Motion data for emulators like Cemu and Dolphin (Cemuhook/DSU).

### Joy-Con and Wii
- Two Joy-Con automatically become one controller; holding one sideways and pressing SL or SR makes it its own player,
  pressing L + R together joins them again.
- **Joy-Con 2 as a mouse:** stand it on its rail edge.
- Read amiibo, Ring-Con and IR camera (Switch Joy-Con), Nunchuk and Classic Controller (Wii).

### Desktop mode (under *General*, off by default)
- When no game is running, the controller controls Windows: left stick = mouse, right stick = scroll, A = click,
  B = right-click, X = task view, Y = on-screen keyboard, D-pad = arrow keys, LB/RB = back/forward, Start = Enter,
  Back = Esc, HOME = Start menu, hold RT = precise pointer.
- Switches off automatically in full-screen games and programs with their own profile. Hold **Back + Start** for
  1 second to pause it.

### Comfort
- Starts with Windows, hidden in the notification area; its menu offers vibrate, player slot and disconnect per controller.
- Pop-up on connect (player, controller, battery – can be turned off), notification on disconnect, automatic
  disconnect after inactivity (saves battery).
- **Test controller** ("Test" tab on every card): sticks with roundness display, triggers, motion sensors, buttons,
  report rate, rumble.
- Ready-made button actions, e.g. for the C button: mute microphone, Discord mute/deafen, show desktop, next/previous track.
- **Export diagnostics** (*General → Advanced*): log and system info as a ZIP for bug reports.
- Original controllers are hidden from Steam and games, so no controller shows up twice.
- Take over pairing data from the Switch or move it to another PC.
- Easy to read at any display scaling (100 % to 200 %).

</details>

## Supported controllers

| Controller | USB cable | Bluetooth | Highlights |
|---|:---:|:---:|---|
| Switch 2 Pro Controller | ✅ | ✅ | gyro, HD rumble, extra buttons GL/GR/C |
| Joy-Con 2 | – | ✅ | as a pair or single, mouse mode |
| GameCube controller (Switch 2) | ✅ | ✅ | analog triggers |
| Switch Pro Controller | ✅ | ✅ | gyro, rumble, amiibo |
| Joy-Con (Switch) | charging grip | ✅ | amiibo, Ring-Con, IR camera |
| NES · SNES · N64 · Genesis (Nintendo Switch Online) | – | ✅ | original button layout |
| Wii Remote (also Plus) | – | ✅ | with Nunchuk or Classic Controller |
| Wii U Pro Controller | – | ✅ | |
| DualShock 4 · DualSense · DualSense Edge | ✅ | ✅ | touchpad, gyro, light bar |
| Xbox 360 · One · Series · Elite | ✅ | ✅ | works directly, N-Connect shows battery and mapping |
| Wired controllers from HORI, PowerA, PDP; clones (e.g. 8BitDo) in Switch mode | ✅ | depends on model | |

> [!NOTE]
> **Do you have a wired controller from HORI, PowerA or PDP?** These models haven't been tested with real hardware
> yet. Please [let us know](https://github.com/DevCatSKZ/N-Connect/issues) whether yours works – ideally with
> *General → Advanced → Export diagnostics*.

> [!NOTE]
> **Lots of controllers at once?** Simple Bluetooth dongles often handle only 2–3 controllers. For more, use a good
> adapter (e.g. Intel AX200/AX210 or a Bluetooth 5.3 dongle).

## Troubleshooting

<details>
<summary><b>The Switch 2 controller won't connect</b></summary>

Press the SYNC button only **briefly**, don't hold it. If the controller is listed in the Windows Bluetooth settings,
**remove** it there. Put a nearby Switch 2 to sleep so it doesn't grab the controller.
</details>

<details>
<summary><b>A Switch, Wii or NSO controller isn't found</b></summary>

Click **"Search for controllers …"** in N-Connect, then press the SYNC button while it searches. The automatic search
pauses while someone is playing.
</details>

<details>
<summary><b>Steam or a game shows a controller twice</b></summary>

Turn on **General → Hide original controllers**, confirm the Windows prompt and restart Steam once.
</details>

<details>
<summary><b>A Joy-Con suddenly became its own player</b></summary>

It was split from the pair (held sideways while pressing SL/SR). Press **L** on the left and **R** on the right
Joy-Con at the same time – and they're a pair again.
</details>

<details>
<summary><b>Message "ViGEmBus driver missing"</b></summary>

Simply run the setup again. This driver is what lets games see the controller.
</details>

<details>
<summary><b>Something else doesn't work</b></summary>

Use **General → Advanced → Export diagnostics** to create a ZIP file and attach it to a
[bug report](https://github.com/DevCatSKZ/N-Connect/issues). It contains the log, settings and system info, but no
pairing keys.
</details>

## Portable version

Prefer no installation? The [release page](https://github.com/DevCatSKZ/N-Connect/releases/latest) also has
**`N-Connect-Portable-….zip`**: unzip it and start `N-Connect.exe`. Settings stay in the folder next to the program.
The two drivers [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) and
[HidHide](https://github.com/nefarius/HidHide/releases) still need to be installed on the PC once.

<details>
<summary><b>For developers: technology and building</b></summary>

N-Connect talks to the controllers directly (Bluetooth LE via WinRT, HID, WinUSB) and passes the input to
[ViGEmBus](https://github.com/nefarius/ViGEmBus), a signed driver for virtual Xbox 360 / DualShock 4 controllers.
[HidHide](https://github.com/nefarius/HidHide) hides the original controllers from games.
Detailed documentation (features, architecture, protocols, in German): **[docs/](docs/README.md)**.

```
src/Switch2Pro.Protocol   Protocols (platform-independent, with tests)
src/Switch2Pro.Bridge     Windows app: connections, virtual controllers, UI, translations
tests/                    xUnit tests
installer/                Inno Setup script
```

**Build** (.NET 8 SDK):
`dotnet test` and
`dotnet publish src/Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out/publish/win-x64`.
Local setup: Inno Setup 6, then `ISCC.exe installer\N-Connect.iss`. Releases are built automatically by
`.github/workflows/build.yml` when a `v…` tag is pushed.

**Check helpers:** `N-Connect.exe --render-ui <folder>` renders every page (runs on an invisible desktop; switches
`--demo`, `--demo-all`, `--light`, `--lang=xx`, `--scheme=xx`, `--scale=1.5`, `--compact`, `--wide`).
`--render <folder>` draws all controller graphics, `--render-brand <folder>` the icon and installer images.

Files: log `%LOCALAPPDATA%\N-Connect\bridge.log`, settings `%APPDATA%\N-Connect\settings.json`.

**Sources and thanks** (protocol information, own implementation):
[ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research), SDL (zlib),
dekuNukem/Nintendo_Switch_Reverse_Engineering, Linux `hid-nintendo` and `hid-wiimote`, WiiBrew, yuzu/Citron,
Dolphin, Switch2Connect, NS2Pro-Bridge-Windows (MIT). Virtual controllers: ViGEmBus and HidHide by Nefarius.
</details>

## License

[MIT](LICENSE) © **devcatskz** – free, open source.

Unofficial project, not affiliated with Nintendo, Sony or Microsoft. "Nintendo", "Switch", "Wii", "amiibo" are
trademarks of Nintendo; "PlayStation", "DualShock", "DualSense" of Sony; "Xbox" of Microsoft; "SEGA" and "Genesis"
of SEGA.
