Playnite Extension: Steam Shortcuts Importer

Overview
- Imports non-Steam games from Steam's `shortcuts.vdf` into Playnite as a library.
- Provides manual sync both ways: Steam → Playnite and Playnite → Steam.
- Lets you configure the Steam root path; the plugin locates `userdata/<steamid>/config/shortcuts.vdf`.

Status
- Targets Playnite 10 (net462). Includes a minimal binary VDF (KeyValues1) reader/writer tailored to `shortcuts.vdf`.

Build
- CLI:
  - `dotnet restore src/SteamShortcutsImporter/SteamShortcutsImporter.csproj`
  - `dotnet build src/SteamShortcutsImporter/SteamShortcutsImporter.csproj -c Release`
- CI builds the `.pext` on Windows.

Usage
- In Playnite, enable the library plugin.
- In settings, set Steam’s root folder (e.g., `C:\\Program Files (x86)\\Steam`).
- Menu actions under “Steam Shortcuts”:
  - “Sync Steam → Playnite…” imports shortcuts as games.
  - “Sync Playnite → Steam…” writes selected Playnite games to `shortcuts.vdf`.

Notes
- The parser covers common fields used by Steam shortcuts: `appname`, `exe`, `StartDir`, `icon`, `ShortcutPath`, `LaunchOptions`, `IsHidden`, `AllowDesktopConfig`, `AllowOverlay`, `OpenVR`, and `tags`.
- Shortcut appids are derived via CRC32 and used to construct `steam://rungameid/<gameid64>` when launch-via-Steam is enabled.
- The plugin finds `shortcuts.vdf` under `userdata/<steamid3>/config` beneath your Steam root.

macOS build
- Install .NET SDK.
- Build with Windows targeting packs:
  - `dotnet restore src/SteamShortcutsImporter/SteamShortcutsImporter.csproj`
  - `dotnet build src/SteamShortcutsImporter/SteamShortcutsImporter.csproj -c Release -p:EnableWindowsTargeting=true`

Local CLI harness (no Playnite dependency)
- A small console app validates reading/writing `shortcuts.vdf`:
  - `dotnet run --project tools/ShortcutsCli -- write-sample /tmp/shortcuts_sample.vdf`
  - `dotnet run --project tools/ShortcutsCli -- read /tmp/shortcuts_sample.vdf`
  - `dotnet run --project tools/ShortcutsCli -- roundtrip /path/to/shortcuts.vdf /tmp/shortcuts_rt.vdf`
- This CLI compiles and runs on macOS and Windows.
