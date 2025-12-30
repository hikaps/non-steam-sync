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

## Releasing

When creating a new release, follow these steps **in order**:

1. **Update version in manifests** (before tagging):
   - `src/SteamShortcutsImporter/extension.yaml` — update `Version: X.Y.Z`
   - `manifests/installer.yaml` — add new package entry at the top with:
     - Version, ReleaseDate, PackageUrl, Changelog

2. **Commit manifest updates** to `develop`, then merge to `main`

3. **Create annotated tag** with markdown release notes:
   ```bash
   git tag -a vX.Y.Z -m "## What's New

   ### Features
   - Feature 1
   - Feature 2

   ### Bug Fixes
   - Fix 1

   ### Other
   - Improvement 1"
   ```

4. **Push tag** to trigger release workflow:
   ```bash
   git push origin vX.Y.Z
   ```

5. **Verify** the release workflow completes and `.pext` is attached to the GitHub release

6. **Don't auto-close issues** — ask users to test before closing issue tickets

### Fixing a bad release tag

If you need to recreate a tag (e.g., forgot to update manifests):

```bash
# Delete local and remote tag
git tag -d vX.Y.Z
git push origin --delete vX.Y.Z

# Make fixes, commit, push
# Then recreate the tag
git tag -a vX.Y.Z -m "release notes..."
git push origin vX.Y.Z
```
