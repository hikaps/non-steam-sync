# Repository Guidelines

## Project Structure & Module Organization
- `src/SteamShortcutsImporter/`: Playnite 10 library plugin (net462, SDK 6.12.0).
  - `Plugin.cs`: core logic, UI dialogs, settings, sync.
  - `ShortcutsFile.cs`: Steam `shortcuts.vdf` binary KV reader/writer + appid/gameid helpers.
  - `extension.yaml`, `icon.png`: extension manifest and icon.
- `tests/ShortcutsTests/`: xUnit tests (parser + helpers).
- `.github/workflows/`: CI for plugin build (Windows) and tests (Ubuntu).
- `shortcuts.vdf`: sample file for tests/dev.

## Build, Test, and Development Commands
- Build plugin (Windows host):
  - `dotnet restore src/SteamShortcutsImporter/SteamShortcutsImporter.csproj`
  - `dotnet build src/SteamShortcutsImporter/SteamShortcutsImporter.csproj -c Release`
- Run tests:
  - `dotnet test tests/ShortcutsTests/ShortcutsTests.csproj -c Release`
- Package .pext (on Windows): output DLL at `src/SteamShortcutsImporter/bin/Release/net462/` and zip with `extension.yaml` (CI does this).

## Coding Style & Naming Conventions
- C# conventions, 4‑space indentation, PascalCase types/methods, camelCase locals.
- Keep changes focused; avoid preview language features (target is net462).
- Use `ObservableCollection<GameAction>` for Playnite `Game.GameActions` and avoid null‑conditional assignment.
- Prefer small, testable helpers (e.g., BinaryKV, appid, gameid64) in `ShortcutsFile.cs`.

## Testing Guidelines
- Framework: xUnit.
- Place tests under `tests/ShortcutsTests/`; name files `*Tests.cs`, methods `[Fact]`/`[Theory]`.
- Include fixtures against the sample `shortcuts.vdf` and cover:
  - BinaryKV parse/round‑trip.
  - appid derivation and 64‑bit `rungameid` computation.

## Commit & Pull Request Guidelines
- Use Conventional Commits when possible: `feat:`, `fix:`, `chore:`, `docs:`.
- PRs should include:
  - Summary of changes and rationale.
  - Screenshots/GIFs for UI changes (dialogs, settings).
  - Notes on testing (commands run, scenarios covered).
- Keep PRs minimal and atomic; avoid unrelated refactors.

## Architecture Notes & Tips
- Launch via Steam uses URL `steam://rungameid/<gameid64>` where `gameid64 = (appid << 32) | 0x02000000`.
- Sync writes both metadata and artwork to Steam grid; respect user setting to launch via Steam.
- Settings persist via Playnite; avoid saving during JSON deserialization; guard nulls.
