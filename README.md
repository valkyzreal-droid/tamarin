# Tamarin

A small, free notepad for Windows. Press a shortcut in any program, type, close. It saves as you type and keeps your notes on your PC.

**Status: early (v0.1).** Built for Windows 10 and 11 (x64); tested on Windows 10 so far. The `.exe` is not code-signed, so SmartScreen will warn: *More info → Run anyway*.

- Global shortcut (default Ctrl+Shift+Space, configurable)
- Autosave, search that ignores accents, trash with restore
- Light, dark and high-contrast themes, resizable window
- Daily backups, Markdown/text import and export, portable mode (`portable.flag`)
- No account, no telemetry, no network code. Notes live in `%APPDATA%\Tamarin`.

Planned, not built yet: notes as plain `.md` files in a folder, memory numbers on more PCs.

## Download
Latest build: [Releases](../../releases/latest).

## Build
See [BUILDING.md](BUILDING.md). Needs the .NET 8 SDK.

## Licence
MIT. Font: Inter (SIL OFL), see `src/Tamarin.App/Assets/Fonts/OFL-Inter.txt`.
