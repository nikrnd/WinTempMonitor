# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-11-11

### Added
- Option to keep the tray icon always visible on the taskbar instead of inside the overflow
  arrow (Windows 11), enabled by default and toggleable from the context menu.
- Pinned window can be dragged anywhere on screen and stays there; its position and pinned
  state are saved in `%APPDATA%\WinTemp\settings.json` and restored on next launch.
- Close button (✕) on the pinned window.

### Changed
- Redesigned popup: headline temperature, one card per component with a colored type label,
  level bars for temperatures and loads, clearer footer hints.
- Dark context menu matching the popup.
- Load sensors renamed ("Utilizzo CPU", "Utilizzo GPU", "Memoria in uso") so they no longer
  share names with temperature sensors.
- Build script downloads the PawnIO driver installer and verifies its SHA256 hash.

## [1.0.0] - 2026-11-11

### Added
- Tray app showing the CPU temperature as a color-coded number in the icon.
- Hover popup listing every temperature sensor (CPU, GPU, motherboard, RAM, storage),
  fan speeds and CPU/GPU load, with current and maximum values.
- Left click to keep the popup open; right-click menu with "Avvia con Windows" and "Esci".
- Start at logon through Task Scheduler (no UAC prompt on each login).
- Inno Setup installer with optional autostart and optional PawnIO driver installation.
- `--dump <file>` diagnostic mode that writes the current readings and popup previews.
