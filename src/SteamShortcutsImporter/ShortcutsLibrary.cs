using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace SteamShortcutsImporter;

public class ShortcutsLibrary : LibraryPlugin
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private static ShortcutsLibrary? Instance;

    public readonly PluginSettings Settings;
    private readonly Guid pluginId = Guid.Parse(Constants.PluginId);
    private readonly ArtworkManager _artworkManager;
    private SteamPathResolver _pathResolver;
    private SelectionDialogBuilder _dialogBuilder;
    
    // Debouncing for Games_ItemUpdated to prevent race conditions
    private Timer? _updateDebounceTimer;
    private readonly object _updateLock = new object();
    private bool _hasPendingUpdates = false;
    private const int UpdateDebounceMs = 2000; // 2 second delay after last update

    public ShortcutsLibrary(IPlayniteAPI api) : base(api)
    {
        Instance = this;
        _artworkManager = new ArtworkManager(api);
        _dialogBuilder = new SelectionDialogBuilder(api, Logger, Name);
        try
        {
            Settings = new PluginSettings(this);
            _pathResolver = new SteamPathResolver(Settings.SteamRootPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize plugin settings.");
            Settings = new PluginSettings(this) { SteamRootPath = string.Empty };
            _pathResolver = new SteamPathResolver(Settings.SteamRootPath);
        }

        try
        {
            // Listen for game updates to sync back changes (write-back)
            if (PlayniteApi != null && PlayniteApi.Database != null)
            {
                PlayniteApi.Database.Games.ItemUpdated += Games_ItemUpdated;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to attach Games.ItemUpdated handler.");
        }
    }

    public override void Dispose()
    {
        // Clean up debounce timer
        _updateDebounceTimer?.Dispose();
        _updateDebounceTimer = null;
        
        base.Dispose();
    }

    public override Guid Id => pluginId;

    public override string Name => Constants.PluginName;

    public override ISettings GetSettings(bool firstRunSettings) => Settings;

    public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
    {
        var view = new PluginSettingsView { DataContext = Settings };
        return view;
    }

    internal string GetBackupRootDir()
    {
        try
        {
            return Path.Combine(GetPluginUserDataPath(), Constants.BackupsDirectory);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to get backup root dir.");
            return string.Empty;
        }
    }

    internal static string? TryGetBackupRootStatic()
    {
        return Instance?.GetBackupRootDir();
    }

    /// <summary>
    /// Gets the backup folder path for a specific Steam user.
    /// </summary>
    internal string GetBackupFolderForUser(string userId)
    {
        return Path.Combine(GetBackupRootDir(), userId);
    }

    /// <summary>
    /// Gets the backup folder for a specific Steam user (static version for settings view).
    /// </summary>
    internal static string? TryGetBackupFolderForUserStatic(string userId)
    {
        return Instance?.GetBackupFolderForUser(userId);
    }

    /// <summary>
    /// Gets all Steam user IDs from the userdata directory.
    /// </summary>
    internal List<string> GetSteamUserIds()
    {
        var result = new List<string>();
        try
        {
            var root = Settings.SteamRootPath;
            if (string.IsNullOrWhiteSpace(root)) return result;

            var userdata = Path.Combine(root, Constants.UserDataDirectory);
            if (!Directory.Exists(userdata)) return result;

            foreach (var userDir in Directory.EnumerateDirectories(userdata))
            {
                var userId = Path.GetFileName(userDir);
                // Filter out non-numeric directories (e.g., "ac" for anonymous)
                if (!string.IsNullOrEmpty(userId) && userId.All(char.IsDigit))
                {
                    result.Add(userId);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to enumerate Steam user IDs.");
        }
        return result;
    }

    /// <summary>
    /// Gets all Steam user IDs (static version for settings view).
    /// </summary>
    internal static List<string> GetSteamUserIdsStatic()
    {
        return Instance?.GetSteamUserIds() ?? new List<string>();
    }

    /// <summary>
    /// Gets the shortcuts.vdf path for a specific Steam user.
    /// </summary>
    internal string? GetShortcutsVdfPathForUser(string userId)
    {
        try
        {
            var root = Settings.SteamRootPath;
            if (string.IsNullOrWhiteSpace(root)) return null;

            var vdfPath = Path.Combine(root, Constants.UserDataDirectory, userId, Constants.ConfigDirectory, "shortcuts.vdf");
            return vdfPath;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed to get shortcuts.vdf path for user {userId}.");
            return null;
        }
    }

    /// <summary>
    /// Gets the shortcuts.vdf path for a specific Steam user (static version for settings view).
    /// </summary>
    internal static string? GetShortcutsVdfPathForUserStatic(string userId)
    {
        return Instance?.GetShortcutsVdfPathForUser(userId);
    }

    /// <summary>
    /// Restores a backup file to the shortcuts.vdf for the specified user.
    /// Creates a backup of the current shortcuts.vdf before restoring.
    /// </summary>
    internal bool RestoreBackup(string backupFilePath, string userId)
    {
        try
        {
            if (!File.Exists(backupFilePath))
            {
                Logger.Warn($"Backup file not found: {backupFilePath}");
                return false;
            }

            var targetVdfPath = GetShortcutsVdfPathForUser(userId);
            if (string.IsNullOrEmpty(targetVdfPath))
            {
                Logger.Warn($"Could not determine shortcuts.vdf path for user {userId}");
                return false;
            }

            // Create backup of current file before restoring (if it exists)
            if (File.Exists(targetVdfPath))
            {
                CreateManagedBackup(targetVdfPath!, userId);
            }

            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetVdfPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Restore the backup
            File.Copy(backupFilePath, targetVdfPath, overwrite: true);
            Logger.Info($"Restored backup '{backupFilePath}' to '{targetVdfPath}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to restore backup '{backupFilePath}'");
            return false;
        }
    }

    /// <summary>
    /// Restores a backup file (static version for settings view).
    /// </summary>
    internal static bool RestoreBackupStatic(string backupFilePath, string userId)
    {
        return Instance?.RestoreBackup(backupFilePath, userId) ?? false;
    }

    /// <summary>
    /// Gets the Playnite API instance (static version for settings view).
    /// </summary>
    internal static IPlayniteAPI? GetPlayniteApiStatic()
    {
        return Instance?.PlayniteApi;
    }

    private void CreateManagedBackup(string sourceFilePath, string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;
            
            // Use new structure: backups/{userId}/
            var dir = GetBackupFolderForUser(userId);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);

            string ts = DateTime.Now.ToString(Constants.TimestampFormat);
            string backupName = string.Format(Constants.BackupFilenameFormat, ts);
            string dst = Path.Combine(dir, backupName);
            File.Copy(sourceFilePath, dst, overwrite: true);

            // Keep last 5 backups for this user
            var files = new DirectoryInfo(dir)
                .GetFiles(Constants.BackupFileSearchPattern)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            for (int i = 5; i < files.Count; i++)
            {
                try { files[i].Delete(); } catch (Exception ex) { Logger.Warn(ex, $"Failed to delete old backup '{files[i].FullName}'"); }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed to create managed backup for '{sourceFilePath}'");
        }
    }

    private static string? TryGetSteamUserFromPath(string path)
    {
        try
        {
            var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int idx = Array.FindIndex(parts, p => string.Equals(p, Constants.UserDataDirectory, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < parts.Length)
            {
                return parts[idx + 1];
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to get steam user from path.");
        }
        return null;
    }

    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = Constants.SteamToPlayniteMenuDescription,
            MenuSection = Constants.MenuSection,
            Action = _ => { ShowImportDialog(); }
        };
        yield return new MainMenuItem
        {
            Description = Constants.PlayniteToSteamMenuDescription,
            MenuSection = Constants.MenuSection,
            Action = _ => { ShowAddToSteamDialog(); }
        };
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        if (args.Games?.Any() == true)
        {
            yield return new GameMenuItem
            {
                Description = Constants.GameMenuAddUpdateDescription,
                MenuSection = Constants.GameMenuSection,
                Action = _ =>
                {
                    try { AddGamesToSteam(args.Games); } 
                    catch (Exception ex) { Logger.Error(ex, "Context Add/Update in Steam failed"); }
                }
            };

            yield return new GameMenuItem
            {
                Description = Constants.GameMenuCopyLaunchDescription,
                MenuSection = Constants.GameMenuSection,
                Action = _ =>
                {
                    try
                    {
                        var g = args.Games.First();
                        var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                        string exe = string.Empty;
                        if (act?.Type == GameActionType.File && !string.IsNullOrEmpty(act.Path))
                        {
                            exe = _pathResolver.ExpandPathVariables(g, act.Path) ?? string.Empty;
                        }
                        var appId = Utils.GenerateShortcutAppId(exe, g.Name);
                        var url = $"{Constants.SteamRungameIdUrl}{Utils.ToShortcutGameId(appId)}";
                        System.Windows.Clipboard.SetText(url);
                        PlayniteApi.Dialogs.ShowMessage(Constants.CopyLaunchUrlMessage, Name);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Copy rungameid failed");
                    }
                }
            };

            yield return new GameMenuItem
            {
                Description = Constants.GameMenuOpenGridDescription,
                MenuSection = Constants.GameMenuSection,
                Action = _ =>
                {
                    try
                    {
                        var vdf = _pathResolver.ResolveShortcutsVdfPath();
                        string? grid = null;
                        if (!string.IsNullOrEmpty(vdf))
                        {
                            grid = _artworkManager.TryGetGridDirFromVdf(vdf!);
                        }
                        if (!string.IsNullOrEmpty(grid) && Directory.Exists(grid))
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo { FileName = grid, UseShellExecute = true };
                            System.Diagnostics.Process.Start(psi);
                        }
                        else
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage(Constants.GridFolderNotFoundMessage, Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Open grid folder failed");
                    }
                }
            };
        }
    }

    private void ShowImportDialog()
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage(Constants.SteamPathRequiredMessage, Name);
            return;
        }
        try
        {
            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            var detector = new DuplicateDetector(this, _pathResolver);
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
                    var isAlready = !string.IsNullOrEmpty(sc.StableId) && PlayniteApi.Database.Games.Any(g => g.PluginId == Id && string.Equals(g.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                    return !isAlready && !existsAny;
                },
                isNew: sc =>
                {
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    var isAlready = !string.IsNullOrEmpty(sc.StableId) && PlayniteApi.Database.Games.Any(g => g.PluginId == Id && string.Equals(g.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                    return !isAlready && !existsAny;
                },
                confirmLabel: Constants.ImportConfirmLabel,
                onConfirm: selected =>
                {
                    var imported = ImportShortcutsToPlaynite(selected, vdfPath!, out var skipped);
                    var msg = skipped > 0 ? $"Imported {imported} item(s). Skipped {skipped} existing item(s)." : $"Imported {imported} item(s) from Steam.";
                    PlayniteApi.Dialogs.ShowMessage(msg, Name);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to import from shortcuts.vdf");
            PlayniteApi.Dialogs.ShowErrorMessage($"Import failed: {ex.Message}", Name);
        }
    }

    private int ImportShortcutsToPlaynite(List<SteamShortcut> shortcuts, string vdfPath, out int skipped)
    {
        var existingById = PlayniteApi.Database.Games
            .Where(g => g.PluginId == Id && !string.IsNullOrEmpty(g.GameId))
            .ToDictionary(g => g.GameId, g => g, StringComparer.OrdinalIgnoreCase);

        var newGames = new List<Game>();
        var detector = new DuplicateDetector(this, _pathResolver);
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
                PluginId = Id,
                GameId = id,
                Name = sc.AppName,
                InstallDirectory = string.IsNullOrEmpty(sc.StartDir) ? null : sc.StartDir,
                IsInstalled = true,
                GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(BuildActionsForShortcut(sc))
            };
            
            newGames.Add(g);
        }

        if (newGames.Count > 0)
        {
            PlayniteApi.Database.Games.Add(newGames);
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

    private void ShowAddToSteamDialog()
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage(Constants.SteamPathRequiredMessage, Name);
            return;
        }
        try
        {
            var allGames = PlayniteApi.Database.Games.Where(g => !g.Hidden).ToList();
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
                    var msg = $"Steam shortcuts updated. Created: {res.Added}, Updated: {res.Updated}, Skipped: {res.Skipped}.";
                    PlayniteApi.Dialogs.ShowMessage(msg, Name);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Playnite→Steam sync failed");
            PlayniteApi.Dialogs.ShowErrorMessage($"Failed to sync: {ex.Message}", Name);
        }
    }

    private Dictionary<string, uint> BuildPlayniteIdLookup()
    {
        var byPlayniteId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var kv in Settings.ExportMap)
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

            var label = (name + " — " + target) + (exists ? Constants.SteamTag : string.Empty);
            return new SelectionCandidate(label, hasAction, exists);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed to build selection candidate for '{game?.Name}'.");
            return SelectionCandidate.Empty;
        }
    }

    private void AddGamesToSteam(IEnumerable<Game> games)
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage(Constants.SteamPathRequiredMessage, Name);
            return;
        }

        var res = AddGamesToSteamCore(games);
        var msg = $"Updated shortcuts.vdf. +{res.Added}/~{res.Updated}, skipped {res.Skipped}.";
        PlayniteApi.Dialogs.ShowMessage(msg, Name);
    }

    private sealed class ExportResult { public int Added; public int Updated; public int Skipped; }

    private ExportResult AddGamesToSteamCore(IEnumerable<Game> games)
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            return new ExportResult();
        }

        var shortcuts = File.Exists(vdfPath) ? ShortcutsFile.Read(vdfPath!).ToList() : new List<SteamShortcut>();
        var existing = shortcuts.ToDictionary(s => s.AppId, s => s);
        var byPlayniteId = BuildPlayniteIdLookup();

        int added = 0, updated = 0, skipped = 0;
        foreach (var g in games)
        {
            var (exePath, workDir, name, action) = GetGameActionDetails(g, existing, byPlayniteId);
            if (action == null)
            {
                skipped++;
                continue;
            }

            var appId = CreateOrUpdateShortcut(g, exePath, workDir, name, action, shortcuts, existing, byPlayniteId, ref added, ref updated);
            UpdateShortcutArtwork(g, appId, vdfPath!);
            UpdateGameActions(g, appId, exePath, workDir, action);
        }

        WriteShortcutsWithBackup(vdfPath!, shortcuts);
        return new ExportResult { Added = added, Updated = updated, Skipped = skipped };
    }

    private (string, string?, string, GameAction?) GetGameActionDetails(Game g, Dictionary<uint, SteamShortcut> existing, Dictionary<string, uint> byPlayniteId)
    {
        var fileAction = g.GameActions?.FirstOrDefault(a => a.Type == GameActionType.File);
        var action = fileAction ?? (g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault());
        if (action == null || string.IsNullOrEmpty(action.Path))
        {
            return (string.Empty, null, string.Empty, null);
        }

        string exePath = string.Empty;
        string? workDir = null;
        string name;

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

        if (action.Type == GameActionType.File)
        {
            exePath = _pathResolver.ExpandPathVariables(g, action.Path) ?? string.Empty;
            workDir = _pathResolver.ExpandPathVariables(g, action.WorkingDir);
            if (string.IsNullOrWhiteSpace(workDir) && !string.IsNullOrWhiteSpace(exePath))
            {
                try { workDir = Path.GetDirectoryName(exePath); } catch (Exception ex) { Logger.Warn(ex, "Failed to get directory name from path."); workDir = null; }
            }
            name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;
        }
        else if (action.Type == GameActionType.URL)
        {
            name = string.IsNullOrEmpty(g.Name) ? action.Path : g.Name;
            if (resolvedExistingAppId != 0 && existing.TryGetValue(resolvedExistingAppId, out var prev))
            {
                exePath = prev.Exe ?? string.Empty;
                workDir = string.IsNullOrWhiteSpace(prev.StartDir) ? workDir : prev.StartDir;
            }
            else
            {
                return (string.Empty, null, string.Empty, null);
            }
        }
        else
        {
            return (string.Empty, null, string.Empty, null);
        }

        return (exePath, workDir, name, action);
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
                Settings.ExportMap[appId.ToString()] = g.Id.ToString();
                SavePluginSettings(Settings);
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
            EnsureFileActionForExternalGame(g, exePath, workDir, action.Type == GameActionType.File ? _pathResolver.ExpandPathVariables(g, action.Arguments) : null);
            if (Settings.LaunchViaSteam && appId != 0) { EnsureSteamPlayActionForExternalGame(g, appId); }
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

    
    public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
        {
            Logger.Warn($"shortcuts.vdf not found. SteamRootPath= '{Settings.SteamRootPath}' ResolvedVdf= '{vdfPath}'");
            return Enumerable.Empty<GameMetadata>();
        }

        try
        {
            Logger.Info($"Reading shortcuts from: {vdfPath}");
            var shortcuts = ShortcutsFile.Read(vdfPath!);

            var metas = new List<GameMetadata>();
            var detector = new DuplicateDetector(this, _pathResolver);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sc in shortcuts)
            {
                var existing = FindExistingGameForShortcut(sc);
                if (existing == null)
                {
                    // Avoid duplicating games that already exist in other libraries
                    if (detector.ExistsAnyGameMatch(sc))
                    {
                        continue;
                    }
                }
                var chosenId = existing?.GameId ?? (!string.IsNullOrEmpty(sc.StableId) ? sc.StableId : sc.AppId.ToString());
                if (string.IsNullOrEmpty(chosenId))
                {
                    chosenId = Utils.HashString($"{sc.Exe}|{sc.AppName}");
                }
                if (seenIds.Contains(chosenId))
                {
                    continue;
                }

                var meta = new GameMetadata
                {
                    Name = sc.AppName,
                    GameId = chosenId,
                    InstallDirectory = string.IsNullOrEmpty(sc.StartDir) ? null : sc.StartDir,
                    Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(Constants.WindowsPlatformName) },
                    Tags = new HashSet<MetadataProperty>(),
                    Links = new List<Link>(),
                    IsInstalled = true,
                };

                meta.GameActions = BuildActionsForShortcut(sc);

                metas.Add(meta);
                seenIds.Add(chosenId);
            }

            Logger.Info($"Imported {metas.Count} shortcuts as games.");
            return metas;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read shortcuts.vdf");
            return Enumerable.Empty<GameMetadata>();
        }
    }

    private Game? FindExistingGameForShortcut(SteamShortcut sc)
    {
        try
        {
            if (!string.IsNullOrEmpty(sc.StableId))
            {
                var g = PlayniteApi.Database.Games.FirstOrDefault(x => x.PluginId == Id && string.Equals(x.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                if (g != null) return g;
            }
            if (sc.AppId != 0)
            {
                var idStr = sc.AppId.ToString();
                var g = PlayniteApi.Database.Games.FirstOrDefault(x => x.PluginId == Id && string.Equals(x.GameId, idStr, StringComparison.OrdinalIgnoreCase));
                if (g != null) return g;
            }
            var byName = PlayniteApi.Database.Games.Where(x => x.PluginId == Id && string.Equals(x.Name, sc.AppName, StringComparison.OrdinalIgnoreCase));
            foreach (var g in byName)
            {
                var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                if (act != null && act.Type == GameActionType.File)
                {
                    var exe = (act.Path ?? string.Empty).Trim('"');
                    if (string.Equals(exe, (sc.Exe ?? string.Empty).Trim('"'), StringComparison.OrdinalIgnoreCase))
                    {
                        return g;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed during existing game lookup.");
        }
        return null;
    }

    private GameAction BuildFilePlayAction(SteamShortcut sc, bool isDefault)
    {
        return new GameAction
        {
            Name = Constants.PlayDirectActionName,
            Type = GameActionType.File,
            Path = sc.Exe?.Trim('"'),
            Arguments = sc.LaunchOptions,
            WorkingDir = sc.StartDir,
            IsPlayAction = isDefault
        };
    }

    private GameAction BuildSteamUrlAction(uint appId, bool isDefault)
    {
        var gid = Utils.ToShortcutGameId(appId);
        return new GameAction
        {
            Name = Constants.PlaySteamActionName,
            Type = GameActionType.URL,
            Path = $"steam://rungameid/{gid}",
            IsPlayAction = isDefault
        };
    }

    private List<GameAction> BuildActionsForShortcut(SteamShortcut sc)
    {
        var actions = new List<GameAction>();
        if (Settings.LaunchViaSteam && sc.AppId != 0)
        {
            // Steam URL default, keep direct exe as secondary
            actions.Add(BuildSteamUrlAction(sc.AppId, isDefault: true));
            actions.Add(BuildFilePlayAction(sc, isDefault: false));
        }
        else
        {
            // Only direct exe as default
            actions.Add(BuildFilePlayAction(sc, isDefault: true));
        }
        return actions;
    }

    private void EnsureSteamPlayAction(Game game, SteamShortcut sc)
    {
        try
        {
            if (!Settings.LaunchViaSteam || sc.AppId == 0)
            {
                return;
            }

            var expectedUrl = $"{Constants.SteamRungameIdUrl}{Utils.ToShortcutGameId(sc.AppId)}";
            var current = game.GameActions?.FirstOrDefault(a => a.IsPlayAction);
            var needsUpdate = current == null || current.Type != GameActionType.URL || !string.Equals(current.Path, expectedUrl, StringComparison.OrdinalIgnoreCase);

            if (needsUpdate)
            {
                game.IsInstalled = true;
                var newActions = BuildActionsForShortcut(sc);
                game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(newActions);
                PlayniteApi.Database.Games.Update(game);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed to ensure Steam play action for game '{game.Name}'");
        }
    }

    private void EnsureSteamPlayActionForExternalGame(Game game, uint appId)
    {
        if (appId == 0)
        {
            return;
        }

        var expectedUrl = $"{Constants.SteamRungameIdUrl}{Utils.ToShortcutGameId(appId)}";
        var existing = game.GameActions as IList<GameAction>;
        var changed = GameActionUtilities.EnsureSteamLaunchAction(existing, expectedUrl, out var updated, out _);
        if (!changed)
        {
            return;
        }

        game.IsInstalled = true;
        game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(updated);
        PlayniteApi.Database.Games.Update(game);
    }

    private void EnsureFileActionForExternalGame(Game game, string exePath, string? workDir, string? args)
    {
        try
        {
            var actions = game.GameActions?.ToList() ?? new List<GameAction>();
            var file = actions.FirstOrDefault(a => a.Type == GameActionType.File);
            if (file == null)
            {
                file = new GameAction { Name = Constants.PlayDirectActionName, Type = GameActionType.File };
                actions.Add(file);
            }
            file.Path = exePath;
            file.WorkingDir = workDir;
            file.Arguments = args;
            // Do not change IsPlayAction here; Steam action remains default when enabled.
            game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(actions);
            PlayniteApi.Database.Games.Update(game);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to ensure file action for external game.");
        }
    }


    private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
    {
        // Debounce updates to prevent race conditions when multiple games change rapidly
        // This queues a write operation that executes after UpdateDebounceMs of idle time
        lock (_updateLock)
        {
            _hasPendingUpdates = true;
            
            // Reset timer - this delays execution until UpdateDebounceMs after the LAST update
            _updateDebounceTimer?.Dispose();
            _updateDebounceTimer = new Timer(_ => ProcessPendingGameUpdates(), null, UpdateDebounceMs, Timeout.Infinite);
        }
    }
    
    private void ProcessPendingGameUpdates()
    {
        lock (_updateLock)
        {
            if (!_hasPendingUpdates)
            {
                return;
            }
            
            _hasPendingUpdates = false;
        }
        
        // Persist all pending changes from Playnite back to shortcuts.vdf
        try
        {
            var vdfPath = _pathResolver.ResolveShortcutsVdfPath();
            if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
            {
                return;
            }

            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            var byStable = shortcuts.ToDictionary(s => s.StableId, s => s, StringComparer.OrdinalIgnoreCase);
            var byApp = shortcuts.ToDictionary(s => s.AppId.ToString(), s => s, StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            // Check all games from this library and sync any that changed
            var ourGames = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();
            foreach (var game in ourGames)
            {
                SteamShortcut? sc = null;
                var gid = game.GameId ?? string.Empty;
                if (!string.IsNullOrEmpty(gid))
                {
                    if (!byStable.TryGetValue(gid, out sc))
                    {
                        byApp.TryGetValue(gid, out sc);
                    }
                }
                if (sc == null)
                {
                    continue;
                }

                UpdateShortcutFromGame(game, sc);
                UpdateShortcutArtworkFromGame(game, sc, vdfPath!);

                changed = true;
            }

            if (changed)
            {
                Logger.Info($"Writing debounced updates to shortcuts.vdf for {ourGames.Count} game(s).");
                WriteShortcutsWithBackup(vdfPath!, shortcuts);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to process pending game updates to shortcuts.vdf");
        }
    }

    private void UpdateShortcutFromGame(Game game, SteamShortcut sc)
    {
        sc.AppName = game.Name;
        // Prefer direct file action to refresh exe/args/dir
        var act = game.GameActions?.FirstOrDefault(a => a.Type == GameActionType.File)
                  ?? game.GameActions?.FirstOrDefault(a => a.IsPlayAction)
                  ?? game.GameActions?.FirstOrDefault();
        if (act != null && act.Type == GameActionType.File)
        {
            var exe = _pathResolver.ExpandPathVariables(game, act.Path) ?? sc.Exe;
            var args = _pathResolver.ExpandPathVariables(game, act.Arguments) ?? sc.LaunchOptions;
            var dir = _pathResolver.ExpandPathVariables(game, act.WorkingDir);
            if (string.IsNullOrWhiteSpace(dir))
            {
                try { dir = Path.GetDirectoryName(exe) ?? sc.StartDir; } catch (Exception ex) { Logger.Warn(ex, "Failed to get directory name."); dir = sc.StartDir; }
            }
            sc.Exe = exe;
            sc.LaunchOptions = args;
            sc.StartDir = dir ?? sc.StartDir;
        }

        if (sc.AppId == 0)
        {
            sc.AppId = Utils.GenerateShortcutAppId(sc.Exe, sc.AppName);
        }

        // Normalize play action to Steam URL when enabled
        EnsureSteamPlayAction(game, sc);
    }

    private void UpdateShortcutArtworkFromGame(Game game, SteamShortcut sc, string vdfPath)
    {
        // Export updated artwork to grid and update icon path
        var gridDir = _artworkManager.TryGetGridDirFromVdf(vdfPath);
        _artworkManager.TryExportArtworkToGrid(game, sc.AppId, gridDir);
        var iconPath = _artworkManager.TryGetGridIconPath(sc.AppId, gridDir);
        if (!string.IsNullOrEmpty(iconPath))
        {
            sc.Icon = iconPath!;
        }
    }

    private void WriteShortcutsWithBackup(string vdfPath, List<SteamShortcut> shortcuts)
    {
        // Check if Steam is running and warn user
        if (SteamProcessHelper.IsSteamRunning())
        {
            var result = PlayniteApi.Dialogs.ShowMessage(
                SteamProcessHelper.GetSteamRunningWarning(),
                Name,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning
            );
            
            if (result == System.Windows.MessageBoxResult.No)
            {
                Logger.Info("User cancelled VDF write due to Steam running.");
                return;
            }
            
            Logger.Warn("User proceeded with VDF write despite Steam running.");
        }

        try
        {
            var userId = TryGetSteamUserFromPath(vdfPath) ?? Constants.DefaultUserSegment;
            CreateManagedBackup(vdfPath, userId);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Creating shortcuts.vdf backup failed");
        }

        ShortcutsFile.Write(vdfPath, shortcuts);
    }
}
