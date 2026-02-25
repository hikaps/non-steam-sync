using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace SteamShortcutsImporter;

public class ShortcutsLibrary : LibraryPlugin
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private static ShortcutsLibrary? Instance;

    public readonly PluginSettings Settings;
    private readonly Guid pluginId = Guid.Parse(Constants.PluginId);
    private readonly ArtworkManager _artworkManager;
    private readonly SteamPathResolver _pathResolver;
    private readonly SelectionDialogBuilder _dialogBuilder;
    private readonly BackupManager _backupManager;
    private readonly ImportExportService _importExportService;
    private readonly WriteBackHandler _writeBackHandler;

    public ShortcutsLibrary(IPlayniteAPI api) : base(api)
    {
        Instance = this;
        _artworkManager = new ArtworkManager(api);
        _dialogBuilder = new SelectionDialogBuilder(api, Logger, Name);
        _backupManager = new BackupManager(this);
        
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

        _importExportService = new ImportExportService(this, _artworkManager, _pathResolver, _dialogBuilder, _backupManager);
        _writeBackHandler = new WriteBackHandler(Logger, PlayniteApi, pluginId, _pathResolver, _artworkManager, _importExportService, Settings);
    }

    public override void Dispose()
    {
        _writeBackHandler?.Dispose();
        base.Dispose();
    }

    public override Guid Id => pluginId;

    public override string Name => Constants.PluginName;

    public override ISettings GetSettings(bool firstRunSettings) => Settings;

    public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
    {
        var view = new PluginSettingsView(PlayniteApi) { DataContext = Settings };
        return view;
    }

    internal string GetBackupRootDir()
    {
        return _backupManager.GetBackupRootDir();
    }

    internal static string? TryGetBackupRootStatic()
    {
        return BackupManager.InstanceStatic?.GetBackupRootDir();
    }

    /// <summary>
    /// Gets the backup folder path for a specific Steam user.
    /// </summary>
    internal string GetBackupFolderForUser(string userId)
    {
        return _backupManager.GetBackupFolderForUser(userId);
    }

    /// <summary>
    /// Gets the backup folder for a specific Steam user (static version for settings view).
    /// </summary>
    internal static string? TryGetBackupFolderForUserStatic(string userId)
    {
        return BackupManager.InstanceStatic?.GetBackupFolderForUser(userId);
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
                // and "0" which is a system placeholder, not a real Steam user
                if (!string.IsNullOrEmpty(userId) && userId != "0" && userId.All(char.IsDigit))
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
        return _backupManager.RestoreBackup(backupFilePath, userId);
    }

    /// <summary>
    /// Restores a backup file (static version for settings view).
    /// </summary>
    internal static bool RestoreBackupStatic(string backupFilePath, string userId)
    {
        return BackupManager.InstanceStatic?.RestoreBackup(backupFilePath, userId) ?? false;
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
        _backupManager.CreateManagedBackup(sourceFilePath, userId);
    }

    private static string? TryGetSteamUserFromPath(string path)
    {
        return BackupManager.TryGetSteamUserFromPath(path);
    }

    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = Constants.SteamToPlayniteMenuDescription,
            MenuSection = Constants.MenuSection,
            Action = _ => { _importExportService.ShowImportDialog(); }
        };
        yield return new MainMenuItem
        {
            Description = Constants.PlayniteToSteamMenuDescription,
            MenuSection = Constants.MenuSection,
            Action = _ => { _importExportService.ShowAddToSteamDialog(); }
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
                    try { _importExportService.AddGamesToSteam(args.Games); } 
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
                        var vdf = _pathResolver.ResolveShortcutsVdfPathForUser(Settings.SelectedSteamUserId);
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

    
    public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
    {
        var vdfPath = _pathResolver.ResolveShortcutsVdfPathForUser(Settings.SelectedSteamUserId);
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
                    Source = new MetadataNameProperty(Constants.SteamShortcutsSourceName),
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

    internal List<GameAction> BuildActionsForShortcut(SteamShortcut sc)
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

    internal void EnsureSteamPlayActionForExternalGame(Game game, uint appId)
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

    internal void EnsureFileActionForExternalGame(Game game, string exePath, string? workDir, string? args)
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


}
