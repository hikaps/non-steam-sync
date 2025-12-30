# Non-Steam Sync for Playnite

## Why This Exists

I love Playnite as a clean, flexible launcher, but Steam offers the best controller experience: powerful per-game profiles, community layouts, Steam Input, and the overlay. This extension bridges the two so you can start games from Playnite while actually launching them through Steam—getting Playnite's organization and visuals with Steam's controller magic. It's the best of both worlds with minimal fuss.

## Features

- **Two-way sync** between Steam shortcuts and Playnite
- **Artwork support** — covers, icons, and backgrounds are imported/exported automatically
- **Launch via Steam** — use `steam://rungameid/...` URLs for full controller and overlay support
- **Automatic write-back** — edits in Playnite sync back to Steam (name, artwork, play actions)
- **Backup & restore** — automatic backups before changes, with easy restore from settings
- **Multi-user support** — works with multiple Steam profiles on the same machine

## Installation

1. Download the latest `.pext` from [Releases](../../releases)
2. In Playnite: **Add-ons → Install from file…** → select the `.pext`
3. Restart Playnite when prompted

## Setup

1. Open settings: **Add-ons → Extensions → Steam Shortcuts**
2. Set your Steam folder:
   - The path is **auto-detected** from the Windows registry on first run
   - Use **Browse** to pick a folder manually
   - Use **Auto-detect** to re-run registry detection
3. Path validation shows the current status:
   - ✓ **Valid** — Steam folder found with userdata
   - ⚠ **Warning** — folder exists but no userdata found
   - ✗ **Invalid** — folder doesn't exist
4. Optional: Enable **"Launch via Steam"** to use Steam URLs as the default play action

## Usage

### Importing from Steam → Playnite

1. **Main menu → Steam Shortcuts → "Sync Steam → Playnite…"**
2. Preview covers/icons, filter by text, toggle "Only new"
3. Select the shortcuts you want to import
4. Click **Import** — artwork is pulled from Steam's grid folder automatically

### Exporting from Playnite → Steam

1. **Main menu → Steam Shortcuts → "Sync Playnite → Steam…"**
2. Select games with a file-based play action
3. Click **Export** — games are added/updated in `shortcuts.vdf`
4. Covers, icons, and backgrounds are copied to Steam's grid folder

### Automatic Write-back

When enabled, editing a Steam Shortcuts game in Playnite (name, play action, tags, artwork) automatically updates `shortcuts.vdf` and Steam grid files. Changes are debounced to prevent race conditions.

### Backup & Restore

- **Automatic backups**: Before any write to `shortcuts.vdf`, a timestamped backup is created
- **Restore**: In settings, click **"Restore Backup…"** to select a previous backup
- Backups are stored per Steam user: `<plugin-data>/backups/<userId>/`
- The last 5 backups per user are retained

### Context Menu Actions

Right-click any game for quick actions:

- **Add/Update in Steam** — export selected games to Steam shortcuts
- **Copy Steam Launch URL** — copy the `steam://rungameid/...` URL to clipboard
- **Open Steam Grid Folder** — open the artwork folder in Explorer

## Troubleshooting

### "No shortcuts found" or nothing imports

- Check the Steam root path in settings
- The plugin searches `userdata/<steamid>/config/shortcuts.vdf`
- If you have multiple Steam profiles, ensure the intended one has shortcuts

### Games skipped during export

When exporting from Playnite to Steam, some games may be skipped. Common reasons:

- **"No executable found"**: The plugin couldn't detect a game executable. This happens with games from some launchers (Epic, EA, Amazon) that don't expose their exe path to Playnite.
- **"No install directory"**: The game has no install path set in Playnite.
- **"Skipped by user"**: You chose to skip the game when prompted.

**How the plugin finds executables:**

1. Uses the game's existing File action (if set)
2. Falls back to URL action (creates a double-launcher shortcut)
3. For GOG games: parses the `goggame-*.info` manifest
4. Scans the install directory for exe files (filters out common installers/redistributables)
5. If auto-detection fails: prompts you to browse for the executable manually

**To fix skipped games:**

1. Try right-clicking the game in Playnite and adding a manual play action
2. When prompted to browse, select the game's main executable
3. The selection is saved so future exports will work automatically

### "Launch via Steam" doesn't work

- The game needs a known shortcut appid
- New exports compute appids automatically using Steam's CRC32 algorithm
- Try re-exporting the game to Steam

### Artwork didn't copy

- Grid files live under `userdata/<steamid>/config/grid/`
- Ensure the folder exists and is writable
- Supported formats: PNG, JPG, ICO

### Changes not appearing in Steam

- **Close Steam** before making changes — Steam caches `shortcuts.vdf` in memory
- If Steam was running, restart it to see updates

### Steam warns about running

- The plugin detects if Steam is running and warns you
- For best results, close Steam before syncing

## How It Works

- Uses a binary KeyValues reader/writer for Steam's `shortcuts.vdf` format
- Shortcut appids are computed using CRC32 (matching Steam's algorithm)
- The `rungameid` URL format: `steam://rungameid/<gameId>` where gameId encodes the appid
- StableIds (hash of exe+name) ensure consistent matching across syncs

---

## AI Disclosure

This project was developed with assistance from AI tools for code generation, documentation, and debugging.
