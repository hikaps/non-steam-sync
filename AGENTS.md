# Agent Guidelines

## Build & Test Commands
- Build plugin: `dotnet build src/SteamShortcutsImporter/SteamShortcutsImporter.csproj`
- Run all tests: `dotnet test tests/ShortcutsTests/ShortcutsTests.csproj`
- Run single test: `dotnet test tests/ShortcutsTests/ShortcutsTests.csproj --filter "FullyQualifiedName~TestMethodName"`
- Package .pext: `pwsh tools/pack.ps1` (Windows only)

## Code Style
- C# with nullable enabled (`<Nullable>enable</Nullable>`), file-scoped namespaces, latest lang version
- Use `var` for obvious types; explicit types for clarity. Prefer expression-bodied members where concise.
- Naming: `PascalCase` for public members, `_camelCase` for private fields, `camelCase` for locals/params
- Error handling: wrap risky operations in try/catch, log with `LogManager.GetLogger()`, fail gracefully
- Tests: xUnit with `[Fact]` and `[Theory]`/`[InlineData]` for parameterized tests

## Branching & Commits
- All new features land on feature branches from `develop`, not `main`. PR back to `develop`.
- Use Conventional Commits: `type(scope): summary` (e.g., `feat(settings): add browse button`)
- Scopes: `core`, `steam`, `settings`, `ci`, `test`. Keep PRs concise and tightly scoped.

## Architecture Notes
- Plugin targets .NET Framework 4.6.2 (Playnite SDK requirement); tests run on .NET 6.0
- Constants go in `Constants.cs`; avoid magic strings in logic files
