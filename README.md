Playnite Extension: Steam Shortcuts Importer

Overview
- Imports non-Steam games from Steam's `shortcuts.vdf` into Playnite as a library.
- Updates Playnite metadata and syncs changes back to `shortcuts.vdf`.
- Lets you configure the path to the target `shortcuts.vdf` (per Steam profile).

Status
- This is a scaffold suitable for building in Visual Studio/VS Code on Windows with Playnite SDK installed.
- Includes a minimal binary VDF (KeyValues1) reader/writer tailored to `shortcuts.vdf`.

Build
- Open `src/SteamShortcutsImporter/SteamShortcutsImporter.csproj` in Visual Studio 2022+.
- Restore NuGet packages (Playnite.SDK).
- Build in Release; copy the resulting `.pext` or the folder with `manifest.yaml` + DLL into Playnite’s Extensions folder.

Usage
- In Playnite, enable the library plugin.
- Set `shortcuts.vdf` path in extension settings.
- Run a library import to create/update entries.
- Edit game metadata in Playnite; plugin listens for updates and writes changes back to `shortcuts.vdf`.

Notes
- The parser covers common fields used by Steam shortcuts: `appname`, `exe`, `StartDir`, `icon`, `ShortcutPath`, `LaunchOptions`, `IsHidden`, `AllowDesktopConfig`, `AllowOverlay`, `OpenVR`, and `tags`.
- App IDs for shortcuts are computed by Steam; this file does not store them. The plugin derives a stable ID used internally.
- If multiple profiles exist, point the path to the correct profile’s `userdata/<steamid3>/config/shortcuts.vdf`.

macOS build
- Install .NET 6 SDK (`brew install dotnet-sdk` or from Microsoft).
- You can compile the plugin on macOS using Windows targeting packs:
  - `dotnet restore src/SteamShortcutsImporter/SteamShortcutsImporter.csproj`
  - `dotnet build src/SteamShortcutsImporter/SteamShortcutsImporter.csproj -c Release -p:EnableWindowsTargeting=true`
- You can’t run Playnite on macOS, but this produces the DLL you can copy to a Windows machine.

Local CLI harness (no Playnite dependency)
- A small console app validates reading/writing `shortcuts.vdf`:
  - `dotnet run --project tools/ShortcutsCli -- write-sample /tmp/shortcuts_sample.vdf`
  - `dotnet run --project tools/ShortcutsCli -- read /tmp/shortcuts_sample.vdf`
  - `dotnet run --project tools/ShortcutsCli -- roundtrip /path/to/shortcuts.vdf /tmp/shortcuts_rt.vdf`
- This CLI compiles and runs on macOS and Windows.
