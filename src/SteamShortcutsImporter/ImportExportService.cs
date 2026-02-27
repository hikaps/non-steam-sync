using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace SteamShortcutsImporter;

internal class ImportExportService
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private readonly ShortcutsLibrary _library;
    private readonly ArtworkManager _artworkManager;
    private readonly SteamPathResolver _pathResolver;
    private readonly SelectionDialogBuilder _dialogBuilder;
    private readonly BackupManager _backupManager;

    internal ImportExportService(
        ShortcutsLibrary library,
        ArtworkManager artworkManager,
        SteamPathResolver pathResolver,
        SelectionDialogBuilder dialogBuilder,
        BackupManager backupManager)
    {
        _library = library;
        _artworkManager = artworkManager;
        _pathResolver = pathResolver;
        _dialogBuilder = dialogBuilder;
        _backupManager = backupManager;
    }

    /// <summary>
    /// Shows the import dialog for Steam shortcuts to Playnite.
    /// </summary>
    public void ShowImportDialog()
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPathForUser(_library.Settings.SelectedSteamUserId);
        if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
        {
            _library.PlayniteApi.Dialogs.ShowErrorMessage(Constants.SteamPathRequiredMessage, _library.Name);
            return;
        }
        try
        {
            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            var detector = new DuplicateDetector(_library, _pathResolver);
            var gridDir = _artworkManager.TryGetGridDirFromVdf(vdfPath!);

            _dialogBuilder.ShowSelectionDialog(
                title: Constants.ImportDialogTitle,
                items: shortcuts.OrderBy(s => s.AppName, StringComparer.OrdinalIgnoreCase).ToList(),
                displayText: sc =>
                {
                    var target = LooksLikeUrl(sc.LaunchOptions) ? sc.LaunchOptions : (sc.Exe ?? string.Empty).Trim('"');
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    var baseText = $"{sc.AppName} — {target}";
                    return existsAny ? baseText + Constants.PlayniteTag : baseText;
                },
                previewImage: sc => _artworkManager.TryPickGridPreview(sc.AppId, gridDir),
                isInitiallyChecked: sc =>
                {
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    var isAlready = !string.IsNullOrEmpty(sc.StableId) && _library.PlayniteApi.Database.Games.Any(g => g.PluginId == _library.Id && string.Equals(g.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                    return !isAlready && !existsAny;
                },
                isNew: sc =>
                {
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    var isAlready = !string.IsNullOrEmpty(sc.StableId) && _library.PlayniteApi.Database.Games.Any(g => g.PluginId == _library.Id && string.Equals(g.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                    return !isAlready && !existsAny;
                },
                confirmLabel: Constants.ImportConfirmLabel,
                onConfirm: selected =>
                {
                    var imported = ImportShortcutsToPlaynite(selected, vdfPath!, out var skipped);
                    var msg = skipped > 0 ? $"Imported {imported} item(s). Skipped {skipped} existing item(s)." : $"Imported {imported} item(s) from Steam.";
                    _library.PlayniteApi.Dialogs.ShowMessage(msg, _library.Name);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to import from shortcuts.vdf");
            _library.PlayniteApi.Dialogs.ShowErrorMessage($"Import failed: {ex.Message}", _library.Name);
        }
    }

    private int ImportShortcutsToPlaynite(List<SteamShortcut> shortcuts, string vdfPath, out int skipped)
    {
        var existingById = _library.PlayniteApi.Database.Games
            .Where(g => g.PluginId == _library.Id && !string.IsNullOrEmpty(g.GameId))
            .ToDictionary(g => g.GameId, g => g, StringComparer.OrdinalIgnoreCase);

        // Get or create the source for Steam Shortcuts
        var source = _library.PlayniteApi.Database.Sources.FirstOrDefault(s => 
            string.Equals(s.Name, Constants.SteamShortcutsSourceName, StringComparison.OrdinalIgnoreCase));
        if (source == null)
        {
            source = new GameSource(Constants.SteamShortcutsSourceName);
            _library.PlayniteApi.Database.Sources.Add(source);
        }

        var newGames = new List<Game>();
        var detector = new DuplicateDetector(_library, _pathResolver);
        skipped = 0;
        foreach (var sc in shortcuts)
        {
            // Skip if an equivalent game already exists in Playnite (any library)
            if (detector.ExistsAnyGameMatch(sc))
            {
                skipped++; continue;
            }
            var id = string.IsNullOrEmpty(sc.StableId) ? sc.AppId.ToString() : sc.StableId;
            if (string.IsNullOrEmpty(id) || existingById.ContainsKey(id))
            {
                skipped++; continue;
            }
            var g = new Game
            {
                PluginId = _library.Id,
                GameId = id,
                Name = sc.AppName,
                SourceId = source.Id,
                InstallDirectory = string.IsNullOrEmpty(sc.StartDir) ? null : sc.StartDir,
                IsInstalled = true,
                GameActions = new ObservableCollection<GameAction>(_library.BuildActionsForShortcut(sc))
            };
            
            newGames.Add(g);
        }

        if (newGames.Count > 0)
        {
            _library.PlayniteApi.Database.Games.Add(newGames);
            try
            {
                var gridDir = _artworkManager.TryGetGridDirFromVdf(vdfPath);
                if (!string.IsNullOrEmpty(gridDir) && Directory.Exists(gridDir))
                {
                    foreach (var g in newGames)
                    {
                        var sc = shortcuts.FirstOrDefault(s => s.StableId == g.GameId || s.AppId.ToString() == g.GameId);
                        if (sc != null)
                        {
                            _artworkManager.TryImportArtworkFromGrid(g, sc.AppId, gridDir!);
                        }
                    }
                }
            }
            catch (Exception aex)
            {
                Logger.Warn(aex, "Artwork import from grid failed.");
            }
        }
        return newGames.Count;
    }

    /// <summary>
    /// Shows the export dialog for Playnite games to Steam.
    /// </summary>
    public void ShowAddToSteamDialog()
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPathForUser(_library.Settings.SelectedSteamUserId);
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            _library.PlayniteApi.Dialogs.ShowErrorMessage(Constants.SteamPathRequiredMessage, _library.Name);
            return;
        }
        try
        {
            var allGames = _library.PlayniteApi.Database.Games.Where(g => !g.Hidden).ToList();
            var shortcuts = File.Exists(vdfPath) ? ShortcutsFile.Read(vdfPath!).ToList() : new List<SteamShortcut>();
            var existingShortcuts = new Dictionary<uint, SteamShortcut>();
            var existingByStable = new Dictionary<string, SteamShortcut>(StringComparer.OrdinalIgnoreCase);
            foreach (var sc in shortcuts)
            {
                existingShortcuts[sc.AppId] = sc;
                if (!string.IsNullOrEmpty(sc.StableId) && !existingByStable.ContainsKey(sc.StableId))
                {
                    existingByStable[sc.StableId] = sc;
                }
            }
            var byPlayniteId = BuildPlayniteIdLookup();

            var detectionById = new Dictionary<Guid, SelectionCandidate>();
            var detectionByRef = new Dictionary<Game, SelectionCandidate>();

            SelectionCandidate GetCandidate(Game game)
            {
                if (game == null)
                {
                    return SelectionCandidate.Empty;
                }

                if (game.Id != Guid.Empty && detectionById.TryGetValue(game.Id, out var cached))
                {
                    return cached;
                }

                if (game.Id == Guid.Empty && detectionByRef.TryGetValue(game, out cached))
                {
                    return cached;
                }

                var computed = BuildSelectionCandidate(game, existingShortcuts, existingByStable, byPlayniteId);
                if (game.Id != Guid.Empty)
                {
                    detectionById[game.Id] = computed;
                }
                else
                {
                    detectionByRef[game] = computed;
                }
                return computed;
            }

            _dialogBuilder.ShowSelectionDialog(
                title: Constants.ExportDialogTitle,
                items: allGames.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                displayText: g =>
                {
                    var candidate = GetCandidate(g);
                    return string.IsNullOrEmpty(candidate.Label)
                        ? $"{g?.Name ?? string.Empty} — "
                        : candidate.Label;
                },
                previewImage: g => _artworkManager.TryPickPlaynitePreview(g),
                isInitiallyChecked: g =>
                {
                    var candidate = GetCandidate(g);
                    return candidate.ShouldSelect;
                },
                isNew: g =>
                {
                    var candidate = GetCandidate(g);
                    return candidate.IsNew;
                },
                confirmLabel: Constants.ExportConfirmLabel,
                onConfirm: selectedGames =>
                {
                    var res = AddGamesToSteamCore(selectedGames);
                    
                    if (res.Added == 0 && res.Updated == 0 && res.Skipped == 0)
                    {
                        return;
                    }

                    if (res.WasSteamRunning)
                    {
                        Logger.Info("Restarting Steam after export...");
                        SteamProcessHelper.TryLaunchSteam(_library.Settings.SteamRootPath);
                    }

                    var msg = $"Steam shortcuts updated. Created: {res.Added}, Updated: {res.Updated}, Skipped: {res.Skipped}.";
                    _library.PlayniteApi.Dialogs.ShowMessage(msg, _library.Name);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Playnite→Steam sync failed");
            _library.PlayniteApi.Dialogs.ShowErrorMessage($"Failed to sync: {ex.Message}", _library.Name);
        }
    }

    /// <summary>
    /// Adds games to Steam shortcuts (for game context menu).
    /// </summary>
    public void AddGamesToSteam(IEnumerable<Game> games)
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPathForUser(_library.Settings.SelectedSteamUserId);
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            _library.PlayniteApi.Dialogs.ShowErrorMessage(Constants.SteamPathRequiredMessage, _library.Name);
            return;
        }

        var res = AddGamesToSteamCore(games);
        
        if (res.Added == 0 && res.Updated == 0 && res.Skipped == 0)
        {
            return;
        }

        if (res.WasSteamRunning)
        {
            Logger.Info("Restarting Steam after export...");
            SteamProcessHelper.TryLaunchSteam(_library.Settings.SteamRootPath);
        }

        var msg = $"Added: {res.Added}, Updated: {res.Updated}";
        if (res.Skipped > 0)
        {
            msg += $", Skipped: {res.Skipped}";
            if (res.SkippedGames.Count > 0 && res.SkippedGames.Count <= 5)
            {
                msg += "\n\nSkipped games:\n• " + string.Join("\n• ", res.SkippedGames);
            }
            else if (res.SkippedGames.Count > 5)
            {
                msg += $"\n\nSkipped games:\n• " + string.Join("\n• ", res.SkippedGames.Take(5));
                msg += $"\n... and {res.SkippedGames.Count - 5} more (see logs for details)";
            }
        }

        _library.PlayniteApi.Dialogs.ShowMessage(msg, _library.Name);
    }

    private Dictionary<string, uint> BuildPlayniteIdLookup()
    {
        var byPlayniteId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var exportMap = _library.Settings?.ExportMap;
            if (exportMap == null)
            {
                return byPlayniteId;
            }

            foreach (var kv in exportMap)
            {
                if (uint.TryParse(kv.Key, out var appId) && !string.IsNullOrEmpty(kv.Value))
                {
                    byPlayniteId[kv.Value] = appId;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to build inverted export map.");
        }

        return byPlayniteId;
    }

    private SelectionCandidate BuildSelectionCandidate(Game game, Dictionary<uint, SteamShortcut> existingById, Dictionary<string, SteamShortcut> existingByStable, Dictionary<string, uint> byPlayniteId)
    {
        if (game == null)
        {
            return SelectionCandidate.Empty;
        }

        try
        {
            var actions = game.GameActions?.Where(a => a != null).ToList() ?? new List<GameAction>();
            var primaryAction = actions.FirstOrDefault(a => a.IsPlayAction) ?? actions.FirstOrDefault();
            var fileAction = actions.FirstOrDefault(a => a.Type == GameActionType.File && !string.IsNullOrEmpty(a.Path));
            var hasAction = actions.Any(a => !string.IsNullOrEmpty(a.Path));

            string exePath = string.Empty;
            if (fileAction != null)
            {
                exePath = _pathResolver.ExpandPathVariables(game, fileAction.Path) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    exePath = fileAction.Path ?? string.Empty;
                }
            }

            string name = !string.IsNullOrEmpty(game.Name)
                ? game.Name
                : (!string.IsNullOrEmpty(exePath)
                    ? Path.GetFileNameWithoutExtension(exePath) ?? string.Empty
                    : string.Empty);

            bool exists = false;

            if (game.Id != Guid.Empty && byPlayniteId.TryGetValue(game.Id.ToString(), out var mappedAppId) && mappedAppId != 0)
            {
                exists = existingById.ContainsKey(mappedAppId);
            }

            if (!exists && actions.Count > 0)
            {
                foreach (var act in actions)
                {
                    if (act?.Type == GameActionType.URL && !string.IsNullOrEmpty(act.Path))
                    {
                        var maybeAppId = SteamPathResolver.TryParseAppIdFromRungameUrl(act.Path);
                        if (maybeAppId != 0)
                        {
                            exists = existingById.ContainsKey(maybeAppId);
                            if (exists)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            if (!exists && hasAction && !string.IsNullOrEmpty(exePath))
            {
                var stableKey = Utils.HashString($"{exePath}|{name}");
                if (existingByStable.TryGetValue(stableKey, out var match))
                {
                    exists = true;
                }
            }

            if (!exists && hasAction && !string.IsNullOrEmpty(exePath))
            {
                var computed = Utils.GenerateShortcutAppId(exePath, name);
                exists = existingById.ContainsKey(computed);
            }

            var displayAction = primaryAction ?? fileAction;
            var target = displayAction != null
                ? (displayAction.Type == GameActionType.File
                    ? (string.IsNullOrEmpty(exePath) ? displayAction.Path ?? string.Empty : exePath)
                    : displayAction.Path ?? string.Empty)
                : string.Empty;

            // Check if this game needs exe discovery (no file action but might be exportable)
            var needsExeDiscovery = fileAction == null && !string.IsNullOrEmpty(game.InstallDirectory);
            
            // Build display label
            string label;
            if (needsExeDiscovery)
            {
                // Show indicator that this game will prompt for exe selection
                label = name + " — " + Constants.ExeNotSetIndicator;
            }
            else
            {
                label = (name + " — " + target) + (exists ? Constants.SteamTag : string.Empty);
            }
            
            // Games needing discovery are considered to have a potential action
            var hasPlayableAction = hasAction || needsExeDiscovery;
            return new SelectionCandidate(label, hasPlayableAction, exists);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed to build selection candidate for '{game?.Name}'.");
            return SelectionCandidate.Empty;
        }
    }

    private bool ConfirmSteamRestart()
    {
        var result = _library.PlayniteApi.Dialogs.ShowMessage(
            Constants.SteamRestartConfirmMessage,
            _library.Name,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question
        );
        
        return result == System.Windows.MessageBoxResult.Yes;
    }

    private ExportResult AddGamesToSteamCore(IEnumerable<Game> games)
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPathForUser(_library.Settings.SelectedSteamUserId);
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            return new ExportResult();
        }

        if (!ConfirmSteamRestart())
        {
            Logger.Info("User cancelled export.");
            return new ExportResult();
        }

        var wasSteamRunning = SteamProcessHelper.IsSteamRunning();
        if (wasSteamRunning)
        {
            Logger.Info("Closing Steam before export...");
            SteamProcessHelper.TryCloseSteam();
        }

        var shortcuts = File.Exists(vdfPath) ? ShortcutsFile.Read(vdfPath!).ToList() : new List<SteamShortcut>();
        var existing = shortcuts.ToDictionary(s => s.AppId, s => s);
        var byPlayniteId = BuildPlayniteIdLookup();

        var context = new ExportContext { SkipAllRemaining = false };

        int added = 0, updated = 0, skipped = 0;
        var skippedGames = new List<string>();

        foreach (var g in games)
        {
            var (exePath, workDir, name, action, result) = GetGameActionDetailsWithFallback(g, existing, byPlayniteId, context);
            
            if (result == ExeDiscoveryResult.UserSkippedAll)
            {
                // User chose "Skip All" - skip remaining games silently
                skipped++;
                skippedGames.Add($"{g.Name}: {Constants.SkipReasonUserSkipped}");
                continue;
            }
            
            if (action == null || result != ExeDiscoveryResult.Success)
            {
                skipped++;
                var reason = GetSkipReason(g, result);
                skippedGames.Add($"{g.Name}: {reason}");
                Logger.Info($"Skipping '{g.Name}': {reason}");
                continue;
            }

            var appId = CreateOrUpdateShortcut(g, exePath, workDir, name, action, shortcuts, existing, byPlayniteId, ref added, ref updated);
            UpdateShortcutArtwork(g, appId, vdfPath!);
            UpdateGameActions(g, appId, exePath, workDir, action);
        }

        WriteShortcutsWithBackup(vdfPath!, shortcuts, context);
        return new ExportResult { Added = added, Updated = updated, Skipped = skipped, SkippedGames = skippedGames, WasSteamRunning = wasSteamRunning };
    }

    private static string GetSkipReason(Game g, ExeDiscoveryResult result)
    {
        return result switch
        {
            ExeDiscoveryResult.UserSkipped => Constants.SkipReasonUserSkipped,
            ExeDiscoveryResult.UserSkippedAll => Constants.SkipReasonUserSkipped,
            _ => string.IsNullOrEmpty(g.InstallDirectory) ? Constants.SkipReasonNoInstallDir : Constants.SkipReasonNoExecutable
        };
    }

    /// <summary>
    /// Gets game action details with fallback chain for games.
    /// Fallback order:
    /// 1. GameActions with File type
    /// 2. GameActions with Emulator type (resolves emulator profile)
    /// 3. GameActions with URL type (double-launcher)
    /// 4. Exe discovery from InstallDirectory
    /// 5. Browse dialog (if ambiguous or not found)
    /// </summary>
    private (string exePath, string? workDir, string name, GameAction? action, ExeDiscoveryResult result) GetGameActionDetailsWithFallback(
        Game g, 
        Dictionary<uint, SteamShortcut> existing, 
        Dictionary<string, uint> byPlayniteId,
        ExportContext context)
    {
        // Check if user chose "Skip All" earlier
        if (context.SkipAllRemaining)
        {
            return (string.Empty, null, string.Empty, null, ExeDiscoveryResult.UserSkippedAll);
        }

        // 1. Try existing File action first
        var fileAction = g.GameActions?.FirstOrDefault(a => a.Type == GameActionType.File && !string.IsNullOrEmpty(a.Path));
        if (fileAction != null)
        {
            var result = BuildFileActionResult(g, fileAction);
            return (result.exePath, result.workDir, result.name, fileAction, ExeDiscoveryResult.Success);
        }

        // 2. Try Emulator action
        var emulatorAction = g.GameActions?.FirstOrDefault(a => a.Type == GameActionType.Emulator);
        if (emulatorAction != null)
        {
            var result = BuildEmulatorActionResult(g, emulatorAction);
            if (result.action != null)
            {
                Logger.Info($"Using Emulator action for '{g.Name}': EmulatorId={emulatorAction.EmulatorId}");
                return (result.exePath, result.workDir, result.name, result.action, ExeDiscoveryResult.Success);
            }
        }

        // 3. Try URL action (double-launcher fallback)
        var urlAction = g.GameActions?.FirstOrDefault(a => a.Type == GameActionType.URL && !string.IsNullOrEmpty(a.Path));
        if (urlAction != null)
        {
            var result = BuildUrlActionResult(g, urlAction, existing, byPlayniteId);
            if (result.action != null)
            {
                Logger.Info($"Using URL action for '{g.Name}': {urlAction.Path}");
                return (result.exePath, result.workDir, result.name, result.action, ExeDiscoveryResult.Success);
            }
        }

        // 4. No valid GameActions - try exe discovery
        if (!string.IsNullOrEmpty(g.InstallDirectory) && Directory.Exists(g.InstallDirectory))
        {
            // First try automatic discovery
            var discoveredExe = _pathResolver.TryDiscoverExecutable(g);
            if (!string.IsNullOrEmpty(discoveredExe))
            {
                var newAction = CreateAndPersistFileAction(g, discoveredExe!);
                var result = BuildFileActionResult(g, newAction);
                return (result.exePath, result.workDir, result.name, newAction, ExeDiscoveryResult.Success);
            }

            // Automatic discovery failed - show browse dialog
            var (selectedExe, userResult) = ShowBrowseForExeDialog(g, context);
            if (userResult == ExeDiscoveryResult.UserSkippedAll)
            {
                context.SkipAllRemaining = true;
                return (string.Empty, null, string.Empty, null, ExeDiscoveryResult.UserSkippedAll);
            }
            if (userResult == ExeDiscoveryResult.UserSkipped || string.IsNullOrEmpty(selectedExe))
            {
                return (string.Empty, null, string.Empty, null, ExeDiscoveryResult.UserSkipped);
            }

            // User selected an exe - persist and use it
            var action = CreateAndPersistFileAction(g, selectedExe!);
            var actionResult = BuildFileActionResult(g, action);
            return (actionResult.exePath, actionResult.workDir, actionResult.name, action, ExeDiscoveryResult.Success);
        }

        // 5. Nothing found - skip
        Logger.Warn($"Cannot export '{g.Name}': No GameActions, no InstallDirectory, or InstallDirectory doesn't exist.");
        return (string.Empty, null, string.Empty, null, ExeDiscoveryResult.NoAction);
    }

    private (string exePath, string? workDir, string name) BuildFileActionResult(Game g, GameAction fileAction)
    {
        var exePath = _pathResolver.ExpandPathVariables(g, fileAction.Path) ?? string.Empty;
        var workDir = _pathResolver.ExpandPathVariables(g, fileAction.WorkingDir);
        
        if (string.IsNullOrWhiteSpace(workDir) && !string.IsNullOrWhiteSpace(exePath))
        {
            try { workDir = Path.GetDirectoryName(exePath); } 
            catch (Exception ex) { Logger.Warn(ex, "Failed to get directory name from path."); workDir = null; }
        }
        
        var name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;
        return (exePath, workDir, name);
    }

    private (string exePath, string? workDir, string name, GameAction? action) BuildUrlActionResult(
        Game g, 
        GameAction urlAction, 
        Dictionary<uint, SteamShortcut> existing,
        Dictionary<string, uint> byPlayniteId)
    {
        var name = string.IsNullOrEmpty(g.Name) ? urlAction.Path : g.Name;
        
        // Check if we have an existing shortcut to pull exe info from
        uint resolvedExistingAppId = 0;
        if (byPlayniteId.TryGetValue(g.Id.ToString(), out var mappedAppId) && mappedAppId != 0)
        {
            resolvedExistingAppId = mappedAppId;
        }
        else
        {
            var maybeAppId = SteamPathResolver.TryParseAppIdFromRungameUrl(urlAction.Path);
            if (maybeAppId != 0)
            {
                resolvedExistingAppId = maybeAppId;
            }
        }

        // If we have an existing shortcut, use its exe/workdir
        if (resolvedExistingAppId != 0 && existing.TryGetValue(resolvedExistingAppId, out var prev))
        {
            var exePath = prev.Exe ?? string.Empty;
            var workDir = string.IsNullOrWhiteSpace(prev.StartDir) ? null : prev.StartDir;
            return (exePath, workDir, name, urlAction);
        }

        // NEW: Allow URL as the "exe" for Steam shortcuts (double-launcher experience)
        // Steam can launch URLs directly
        var url = urlAction.Path ?? string.Empty;
        if (!string.IsNullOrEmpty(url))
        {
            // Use InstallDirectory as working dir if available
            var workDir = !string.IsNullOrEmpty(g.InstallDirectory) && Directory.Exists(g.InstallDirectory) 
                ? g.InstallDirectory 
                : null;
            
            Logger.Info($"Creating URL shortcut for '{g.Name}': {url}");
            return (url, workDir, name, urlAction);
        }

        return (string.Empty, null, name, null);
    }

    /// <summary>
    /// Builds the result tuple for an emulator-based game action.
    /// Resolves the emulator profile to get the executable path, arguments, and working directory.
    /// </summary>
    private (string exePath, string? workDir, string name, GameAction? action) BuildEmulatorActionResult(
        Game g,
        GameAction emulatorAction)
    {
        var name = string.IsNullOrEmpty(g.Name) ? "Emulator Game" : g.Name;

        // Get emulator from database
        var emulator = _library.PlayniteApi.Database.Emulators
            .FirstOrDefault(e => e.Id == emulatorAction.EmulatorId);

        if (emulator == null)
        {
            Logger.Warn($"Cannot export '{g.Name}': Emulator not found (EmulatorId={emulatorAction.EmulatorId})");
            return (string.Empty, null, name, null);
        }

        // Get the profile
        var profile = emulator.GetProfile(emulatorAction.EmulatorProfileId);
        if (profile == null)
        {
            Logger.Warn($"Cannot export '{g.Name}': Emulator profile not found (ProfileId={emulatorAction.EmulatorProfileId})");
            return (string.Empty, null, name, null);
        }

        // Handle CustomEmulatorProfile
        if (profile is CustomEmulatorProfile customProfile)
        {
            return BuildCustomEmulatorResult(g, emulatorAction, customProfile, emulator.InstallDir, name);
        }

        // Handle BuiltInEmulatorProfile
        if (profile is BuiltInEmulatorProfile builtInProfile)
        {
            return BuildBuiltInEmulatorResult(g, emulatorAction, builtInProfile, emulator.InstallDir, emulator.BuiltInConfigId, name);
        }

        Logger.Warn($"Cannot export '{g.Name}': Unknown emulator profile type '{profile.GetType().Name}'");
        return (string.Empty, null, name, null);
    }

    /// <summary>
    /// Builds result for a custom emulator profile.
    /// </summary>
    private (string exePath, string? workDir, string name, GameAction? action) BuildCustomEmulatorResult(
        Game g,
        GameAction emulatorAction,
        CustomEmulatorProfile profile,
        string? emulatorInstallDir,
        string name)
    {
        // Get executable path
        var exePath = profile.Executable ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            Logger.Warn($"Cannot export '{g.Name}': Emulator profile has no executable set");
            return (string.Empty, null, name, null);
        }

        // Build arguments (considering OverrideDefaultArgs and AdditionalArguments)
        string args;
        if (emulatorAction.OverrideDefaultArgs)
        {
            // Use only the action's additional arguments
            args = emulatorAction.AdditionalArguments ?? string.Empty;
        }
        else
        {
            // Combine profile arguments with action's additional arguments
            var profileArgs = profile.Arguments ?? string.Empty;
            var additionalArgs = emulatorAction.AdditionalArguments ?? string.Empty;
            args = string.IsNullOrWhiteSpace(additionalArgs) 
                ? profileArgs 
                : (string.IsNullOrWhiteSpace(profileArgs) ? additionalArgs : $"{profileArgs} {additionalArgs}");
        }

        // Expand variables using Playnite API
        var emulatorDir = emulatorInstallDir ?? string.Empty;
        var expandedExe = _library.PlayniteApi.ExpandGameVariables(g, exePath, emulatorDir);
        var expandedArgs = _library.PlayniteApi.ExpandGameVariables(g, args, emulatorDir);

        // Get working directory
        string? workDir = profile.WorkingDirectory;
        if (!string.IsNullOrWhiteSpace(workDir))
        {
            workDir = _library.PlayniteApi.ExpandGameVariables(g, workDir, emulatorDir);
        }

        // For Steam shortcut, we need to combine exe + args into a launch command
        // Store args separately for the shortcut's LaunchOptions
        // The exePath is used as-is, and args go into LaunchOptions in CreateOrUpdateShortcut

        Logger.Info($"Resolved emulator for '{g.Name}': Exe={expandedExe}, Args={expandedArgs}");

        // We need to store the arguments somewhere since CreateOrUpdateShortcut reads from action.Arguments
        // Create a synthetic File action with the resolved paths
        var syntheticAction = new GameAction
        {
            Name = emulatorAction.Name ?? "Play (Emulator)",
            Type = GameActionType.File,
            Path = expandedExe,
            Arguments = expandedArgs,
            WorkingDir = workDir,
            IsPlayAction = emulatorAction.IsPlayAction
        };

        return (expandedExe, workDir, name, syntheticAction);
    }

    /// <summary>
    /// Builds result for a built-in emulator profile.
    /// Built-in profiles use definitions from Playnite's emulator database.
    /// </summary>
    private (string exePath, string? workDir, string name, GameAction? action) BuildBuiltInEmulatorResult(
        Game g,
        GameAction emulatorAction,
        BuiltInEmulatorProfile profile,
        string? emulatorInstallDir,
        string? builtInConfigId,
        string name)
    {
        // Built-in profiles reference an emulator definition via the emulator's BuiltInConfigId
        if (string.IsNullOrEmpty(builtInConfigId))
        {
            Logger.Warn($"Cannot export '{g.Name}': Emulator has no BuiltInConfigId set");
            return (string.Empty, null, name, null);
        }

        // Get the emulator definition from Playnite's emulation database
        var definition = _library.PlayniteApi.Emulation.GetEmulator(builtInConfigId);
        if (definition == null)
        {
            Logger.Warn($"Cannot export '{g.Name}': Built-in emulator definition not found (Id={builtInConfigId})");
            return (string.Empty, null, name, null);
        }

        // Get the profile definition by name
        var profileDef = definition.Profiles?.FirstOrDefault(p => p.Name == profile.BuiltInProfileName);
        if (profileDef == null)
        {
            Logger.Warn($"Cannot export '{g.Name}': Built-in emulator profile definition not found (Profile={profile.BuiltInProfileName})");
            return (string.Empty, null, name, null);
        }

        // Get executable from the definition
        var exePath = profileDef.StartupExecutable ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            Logger.Warn($"Cannot export '{g.Name}': Built-in emulator profile has no startup executable");
            return (string.Empty, null, name, null);
        }

        // Build arguments - consider both profile.OverrideDefaultArgs and emulatorAction.OverrideDefaultArgs
        string args;
        if (profile.OverrideDefaultArgs)
        {
            // Profile overrides built-in args with its custom args
            args = profile.CustomArguments ?? string.Empty;
        }
        else if (emulatorAction.OverrideDefaultArgs)
        {
            // Action overrides everything with its additional args
            args = emulatorAction.AdditionalArguments ?? string.Empty;
        }
        else
        {
            // Combine built-in args, profile custom args, and action additional args
            var builtInArgs = profileDef.StartupArguments ?? string.Empty;
            var customArgs = profile.CustomArguments ?? string.Empty;
            var additionalArgs = emulatorAction.AdditionalArguments ?? string.Empty;
            
            args = builtInArgs;
            if (!string.IsNullOrWhiteSpace(customArgs))
            {
                args = string.IsNullOrWhiteSpace(args) ? customArgs : $"{args} {customArgs}";
            }
            if (!string.IsNullOrWhiteSpace(additionalArgs))
            {
                args = string.IsNullOrWhiteSpace(args) ? additionalArgs : $"{args} {additionalArgs}";
            }
        }

        // Expand variables
        var emulatorDir = emulatorInstallDir ?? string.Empty;
        var expandedExe = _library.PlayniteApi.ExpandGameVariables(g, exePath, emulatorDir);
        var expandedArgs = _library.PlayniteApi.ExpandGameVariables(g, args, emulatorDir);

        // Working directory - use emulator install dir if not specified
        string? workDir = emulatorInstallDir;

        Logger.Info($"Resolved built-in emulator for '{g.Name}': Exe={expandedExe}, Args={expandedArgs}");

        // Create a synthetic File action with the resolved paths
        var syntheticAction = new GameAction
        {
            Name = emulatorAction.Name ?? "Play (Emulator)",
            Type = GameActionType.File,
            Path = expandedExe,
            Arguments = expandedArgs,
            WorkingDir = workDir,
            IsPlayAction = emulatorAction.IsPlayAction
        };

        return (expandedExe, workDir, name, syntheticAction);
    }

    /// <summary>
    /// Shows a file browse dialog for the user to select the game executable.
    /// </summary>
    private (string? selectedExe, ExeDiscoveryResult result) ShowBrowseForExeDialog(Game game, ExportContext context)
    {
        try
        {
            // Show a message dialog first with Skip/Skip All/Browse options
            var result = _library.PlayniteApi.Dialogs.ShowMessage(
                $"Could not automatically detect the executable for \"{game.Name}\".\n\n" +
                $"Install directory: {game.InstallDirectory}\n\n" +
                "Would you like to browse for the executable?",
                Constants.SelectExeDialogTitle,
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Cancel)
            {
                // Cancel = Skip All
                context.SkipAllRemaining = true;
                return (null, ExeDiscoveryResult.UserSkippedAll);
            }
            
            if (result == System.Windows.MessageBoxResult.No)
            {
                // No = Skip this game
                return (null, ExeDiscoveryResult.UserSkipped);
            }

            // Yes = Browse
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"{Constants.SelectExeDialogTitle} - {game.Name}",
                Filter = Constants.SelectExeDialogFilter,
                InitialDirectory = game.InstallDirectory
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.FileName))
            {
                return (dialog.FileName, ExeDiscoveryResult.Success);
            }

            // User cancelled the file dialog
            return (null, ExeDiscoveryResult.UserSkipped);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error showing browse dialog for '{game.Name}'");
            return (null, ExeDiscoveryResult.NoAction);
        }
    }

    /// <summary>
    /// Creates a new File GameAction and persists it to the game's GameActions.
    /// </summary>
    private GameAction CreateAndPersistFileAction(Game game, string exePath)
    {
        var action = new GameAction
        {
            Name = Constants.PlayDirectActionName,
            Type = GameActionType.File,
            Path = exePath,
            WorkingDir = Path.GetDirectoryName(exePath),
            IsPlayAction = game.GameActions == null || !game.GameActions.Any(a => a.IsPlayAction)
        };

        try
        {
            var actions = game.GameActions?.ToList() ?? new List<GameAction>();
            
            // Insert at beginning so it becomes the primary action
            actions.Insert(0, action);
            
            game.GameActions = new ObservableCollection<GameAction>(actions);
            _library.PlayniteApi.Database.Games.Update(game);
            
            Logger.Info($"Persisted new GameAction for '{game.Name}': {exePath}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to persist GameAction for '{game.Name}'");
        }

        return action;
    }

    

    private uint CreateOrUpdateShortcut(Game g, string exePath, string? workDir, string name, GameAction action, List<SteamShortcut> shortcuts, Dictionary<uint, SteamShortcut> existing, Dictionary<string, uint> byPlayniteId, ref int added, ref int updated)
    {
        uint resolvedExistingAppId = 0;
        if (byPlayniteId.TryGetValue(g.Id.ToString(), out var mappedAppId) && mappedAppId != 0)
        {
            resolvedExistingAppId = mappedAppId;
        }
        else if (action.Type == GameActionType.URL)
        {
            var maybeAppId = SteamPathResolver.TryParseAppIdFromRungameUrl(action.Path);
            if (maybeAppId != 0)
            {
                resolvedExistingAppId = maybeAppId;
            }
        }

        var appId = resolvedExistingAppId != 0 ? resolvedExistingAppId : Utils.GenerateShortcutAppId(exePath, name);
        if (!existing.TryGetValue(appId, out var sc))
        {
            sc = new SteamShortcut { AppName = name, Exe = exePath, StartDir = workDir ?? string.Empty, AppId = appId };
            shortcuts.Add(sc);
            added++;
        }
        else
        {
            sc.AppName = name;
            sc.Exe = exePath;
            sc.StartDir = workDir ?? sc.StartDir;
            updated++;
        }

        if (action.Type == GameActionType.URL)
        {
            sc.LaunchOptions = action.Path;
        }
        else if (action.Type == GameActionType.File)
        {
            sc.LaunchOptions = _pathResolver.ExpandPathVariables(g, action.Arguments) ?? string.Empty;
        }

        try
        {
            if (g.Id != Guid.Empty)
            {
                _library.Settings.ExportMap[appId.ToString()] = g.Id.ToString();
                _library.SavePluginSettings(_library.Settings);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to record export mapping.");
        }

        return appId;
    }

    private void UpdateShortcutArtwork(Game g, uint appId, string vdfPath)
    {
        try
        {
            var gridDir = _artworkManager.TryGetGridDirFromVdf(vdfPath);
            if (!string.IsNullOrEmpty(gridDir))
            {
                _artworkManager.TryExportArtworkToGrid(g, appId, gridDir);
                var iconPath = _artworkManager.TryGetGridIconPath(appId, gridDir);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var sc = ShortcutsFile.Read(vdfPath).FirstOrDefault(s => s.AppId == appId);
                    if (sc != null)
                    {
                        sc.Icon = iconPath!;
                    }
                }
            }
        }
        catch (Exception aex)
        {
            Logger.Warn(aex, "Exporting artwork to grid failed.");
        }
    }

    private void UpdateGameActions(Game g, uint appId, string exePath, string? workDir, GameAction action)
    {
        try
        {
            _library.EnsureFileActionForExternalGame(g, exePath, workDir, action.Type == GameActionType.File ? _pathResolver.ExpandPathVariables(g, action.Arguments) : null);
            if (_library.Settings.LaunchViaSteam && appId != 0) { _library.EnsureSteamPlayActionForExternalGame(g, appId); }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to ensure play action.");
        }
    }

    private static bool LooksLikeUrl(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var val = s!.Trim();
        return val.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || val.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || val.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);
    }

    public void WriteShortcutsWithBackup(string vdfPath, List<SteamShortcut> shortcuts, ExportContext context)
    {
        try
        {
            var userId = BackupManager.TryGetSteamUserFromPath(vdfPath) ?? Constants.DefaultUserSegment;
            _backupManager.CreateManagedBackup(vdfPath, userId);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Creating shortcuts.vdf backup failed");
        }

        ShortcutsFile.Write(vdfPath, shortcuts);
    }

    #region Nested Classes

    private sealed class ExportResult 
    { 
        public int Added; 
        public int Updated; 
        public int Skipped;
        public List<string> SkippedGames = new List<string>();
        public bool WasSteamRunning;
    }

    /// <summary>
    /// Result from attempting to get or discover game action details.
    /// </summary>
    private enum ExeDiscoveryResult
    {
        Success,
        NoAction,
        UserSkipped,
        UserSkippedAll
    }

    private sealed class SelectionCandidate
    {
        public static SelectionCandidate Empty { get; } = new SelectionCandidate(string.Empty, false, false);

        public SelectionCandidate(string label, bool hasPlayableAction, bool existsInSteam)
        {
            Label = label;
            HasPlayableAction = hasPlayableAction;
            ExistsInSteam = existsInSteam;
        }

        public string Label { get; }
        public bool HasPlayableAction { get; }
        public bool ExistsInSteam { get; }
        public bool ShouldSelect => HasPlayableAction && !ExistsInSteam;
        public bool IsNew => ShouldSelect;
    }

    #endregion
}
