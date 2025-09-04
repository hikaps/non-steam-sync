Playnite Extension: Steam Shortcuts Importer

## Why This Exists

I love Playnite as a clean, flexible launcher, but Steam offers the best controller experience: powerful per‑game profiles, community layouts, Steam Input, and the overlay. This extension bridges the two so you can start games from Playnite while actually launching them through Steam—getting Playnite’s organization and visuals with Steam’s controller magic. It’s the best of both worlds with minimal fuss.

What It Does
- Import your non‑Steam games from Steam’s “shortcuts” into Playnite.
- Sync both ways when you want:
  - Steam → Playnite: import selected shortcuts as Playnite games.
  - Playnite → Steam: create/update selected shortcuts in Steam.
- Optional: automatically write your Playnite edits back to Steam (including grid artwork).

Install
- Download the latest `.pext` from Releases (SteamShortcutsImporter-<version>.pext).
- In Playnite: Add‑ons → Install from file… → choose the `.pext`.
- Restart Playnite if prompted.

Setup (first run)
- Add‑ons → Extensions → Steam Shortcuts.
- Set your Steam folder (e.g., `C:\\Program Files (x86)\\Steam`).
- Optional: enable “Launch via Steam” to launch via `steam://rungameid/...` when possible.

How To Use
- Steam → Playnite
  - Main menu → Steam Shortcuts → “Sync Steam → Playnite…”.
  - Preview covers/icons, filter by text, show “Only new”, invert selection; the selected count updates live.
  - Click Import. The extension skips existing items and pulls artwork from Steam’s grid when available.
- Playnite → Steam
  - Main menu → Steam Shortcuts → “Sync Playnite → Steam…”.
  - Pick Playnite games with a file play action to export; existing Steam entries are updated.
  - Covers/icons/backgrounds are exported into Steam’s grid folder.
  - Tags and (optionally) your Playnite Categories are exported as Steam categories, so your shortcuts stay organized in Steam.
- Automatic write‑backs (optional)
  - When enabled, editing a Steam Shortcuts game in Playnite (name/play action/tags/artwork) updates `shortcuts.vdf` and Steam grid files automatically.

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
