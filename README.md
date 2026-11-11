# WinTemp

A lightweight Windows tray app that shows every temperature sensor in your PC.
The tray icon displays the CPU temperature as a color-coded number; hover over it to see
all sensors at a glance.

- **Tray icon** – current CPU temperature (green < 65 °C, amber < 85 °C, red ≥ 85 °C).
  Falls back to the hottest sensor when the CPU temperature is not available.
- **Hover popup** – one card per component (CPU, GPU, motherboard, RAM, disks) with
  temperatures, fan speeds and CPU/GPU load, each with its current and maximum value.
- **Pinned window** – left-click the icon to keep the window open. Drag it anywhere;
  it stays there and is restored at the same position on next launch.
- **Always visible** – the icon is promoted out of the overflow arrow onto the taskbar
  (Windows 11).
- **Start with Windows** – via Task Scheduler, so there is no UAC prompt at each login.

The UI is in Italian.

## Requirements

- Windows 10/11 x64 (the "always visible" option needs Windows 11)
- Administrator rights (required to read hardware sensors)
- [PawnIO](https://pawnio.eu/) driver for CPU and motherboard temperatures – the installer
  can install it for you

.NET does not need to be installed: the app is published self-contained.

## Install

Run `dist\WinTemp-Setup-<version>.exe` (see [Build](#build)) and choose the options:

| Option | Default | Effect |
| --- | --- | --- |
| Start WinTemp at logon | on | Creates the `WinTemp` scheduled task (runs with highest privileges) |
| Install PawnIO driver | on, if missing | Needed for CPU / motherboard temperatures |

Uninstalling removes the app and the scheduled task. PawnIO stays installed (it may be used
by other tools) and can be removed from *Settings → Apps*.

## Usage

| Action | Result |
| --- | --- |
| Hover the tray icon | Shows the sensor popup; it hides when the mouse leaves |
| Left-click the tray icon | Pins / unpins the window |
| Drag the pinned window | Moves it; the position is saved |
| ✕ on the pinned window | Unpins and closes it |
| Right-click the tray icon | Menu: pin window, always visible on taskbar, start with Windows, exit |

Settings are stored in `%APPDATA%\WinTemp\settings.json`.

## Build

Prerequisites:

```bash
winget install Microsoft.DotNet.SDK.8
```

```bash
winget install JRSoftware.InnoSetup
```

Then build the app and the installer:

```bash
powershell -ExecutionPolicy Bypass -File build.ps1
```

The script downloads the PawnIO installer (SHA256-checked), publishes a self-contained
single-file `WinTemp.exe` and compiles the installer into `dist\`.

### Diagnostics

```bash
WinTemp.exe --dump readings.txt
```

Writes the current sensor readings to `readings.txt` plus `readings.png` and
`readings.pinned.png` previews of the popup, then exits.

## Project layout

```
src/WinTemp/
  Program.cs         entry point, single instance, --dump mode
  TrayApp.cs         tray icon, hover/pin logic, context menu
  HoverPopup.cs      owner-drawn sensor window (hover and pinned modes)
  SensorReader.cs    background polling of LibreHardwareMonitor
  TrayPromotion.cs   keeps the icon visible on the Windows 11 taskbar
  Autostart.cs       start-at-logon scheduled task
  Settings.cs        JSON settings in %APPDATA%
installer/WinTemp.iss  Inno Setup script
build.ps1              build + installer script
```

## Credits

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) –
  sensor library (MPL-2.0)
- [PawnIO](https://github.com/namazso/PawnIO.Setup) – kernel driver for low-level hardware access
