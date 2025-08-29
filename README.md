Playnite Extension: Steam Shortcuts Importer

What It Does
- Imports your non‑Steam games from Steam’s “shortcuts” into Playnite.
- Syncs changes both ways on demand:
  - Steam → Playnite: import selected shortcuts as Playnite games.
  - Playnite → Steam: create/update selected shortcuts in Steam.
- Optional: automatically writes changes you make in Playnite back to Steam (including grid artwork).

Requirements
- Windows with Steam installed.
- Playnite 10 (library plugin). Tested with net462 target.

Install
- Download the latest `.pext` from the GitHub Releases page (SteamShortcutsImporter-<version>.pext).
- In Playnite: Add-ons → Install from file… → select the downloaded `.pext`.
- Restart Playnite if prompted.

Setup (first run)
- Open Add-ons → Extensions → Steam Shortcuts.
- Set Steam root folder (for example `C:\\Program Files (x86)\\Steam`).
- Optional: enable “Launch via Steam” to launch through `steam://rungameid/...` when possible.

How To Use
- Steam → Playnite:
  - Main menu → Steam Shortcuts → “Sync Steam → Playnite…”.
  - Use the filter and checkboxes to pick items; duplicates are flagged and left unchecked.
  - Click Import. Artwork is pulled from Steam’s grid when available.
- Playnite → Steam:
  - Main menu → Steam Shortcuts → “Sync Playnite → Steam…”.
  - Pick Playnite games with a file play action to export. Existing Steam entries are updated.
  - Artwork (cover/icon/background) is exported into Steam’s grid folder.
- Automatic write‑backs (optional):
  - When enabled, editing a game from this library in Playnite (name, play action, tags, artwork) will update `shortcuts.vdf` and Steam grid files automatically.

Duplicate Handling
- The importer avoids duplicates by checking:
  - Existing library IDs (stable id/appid string).
  - Name + executable path (resolves `{InstallDir}` and environment variables).
  - Name + Steam URL (`steam://rungameid/<gameid64>`).

Troubleshooting
- “No shortcuts found” or nothing imports:
  - Check the Steam root path in settings. The plugin searches `userdata/<steamid>/config/shortcuts.vdf`.
  - If you have multiple Steam profiles, ensure the intended one has shortcuts.
- “Launch via Steam” doesn’t show:
  - The game needs a known shortcut appid. New exports compute appids automatically.
- Artwork didn’t copy:
  - Grid files live under `…/userdata/<steamid>/config/grid`. Make sure the folder exists and is writable.

Notes
- Uses a small reader/writer for Steam’s binary KeyValues (shortcuts.vdf).
- Shortcut appids are computed (CRC32) and used to build the `rungameid` launch URL.

For Developers
- Build (Windows):
  - `dotnet restore src/SteamShortcutsImporter/SteamShortcutsImporter.csproj`
  - `dotnet build src/SteamShortcutsImporter/SteamShortcutsImporter.csproj -c Release`
- Tests: `dotnet test tests/ShortcutsTests/ShortcutsTests.csproj -c Release`
- A small CLI tool in `tools/ShortcutsCli` helps validate read/write/round‑trip of `shortcuts.vdf`.
