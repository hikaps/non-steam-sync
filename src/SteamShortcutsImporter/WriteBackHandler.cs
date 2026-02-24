using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SteamShortcutsImporter;

internal class WriteBackHandler : IDisposable
{
    private readonly ILogger _logger;
    private readonly IPlayniteAPI? _playniteApi;
    private readonly Guid _pluginId;
    private readonly SteamPathResolver _pathResolver;
    private readonly ArtworkManager _artworkManager;
    private readonly ImportExportService _importExportService;
    private readonly PluginSettings _settings;

    // Debouncing for Games_ItemUpdated to prevent race conditions
    private Timer? _updateDebounceTimer;
    private readonly object _updateLock = new object();
    private bool _hasPendingUpdates = false;
    private const int UpdateDebounceMs = 2000; // 2 second delay after last update

    public WriteBackHandler(
        ILogger logger,
        IPlayniteAPI playniteApi,
        Guid pluginId,
        SteamPathResolver pathResolver,
        ArtworkManager artworkManager,
        ImportExportService importExportService,
        PluginSettings settings)
    {
        _logger = logger;
        _playniteApi = playniteApi;
        _pluginId = pluginId;
        _pathResolver = pathResolver;
        _artworkManager = artworkManager;
        _importExportService = importExportService;
        _settings = settings;

        try
        {
            // Listen for game updates to sync back changes (write-back)
            if (_playniteApi?.Database != null)
            {
                _playniteApi.Database.Games.ItemUpdated += Games_ItemUpdated;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to attach Games.ItemUpdated handler.");
        }
    }

    public void Dispose()
    {
        // Thread-safe cleanup of debounce timer using Interlocked.Exchange
        var timer = Interlocked.Exchange(ref _updateDebounceTimer, null);
        timer?.Dispose();
    }

    private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
    {
        // Debounce updates to prevent race conditions when multiple games change rapidly
        // This queues a write operation that executes after UpdateDebounceMs of idle time
        lock (_updateLock)
        {
            _hasPendingUpdates = true;
            
            // Thread-safe timer replacement - dispose old timer and create new one atomically
            var newTimer = new Timer(_ => ProcessPendingGameUpdates(), null, UpdateDebounceMs, Timeout.Infinite);
            var oldTimer = Interlocked.Exchange(ref _updateDebounceTimer, newTimer);
            oldTimer?.Dispose();
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
            var vdfPath = _pathResolver.ResolveShortcutsVdfPathForUser(_settings.SelectedSteamUserId);
            if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
            {
                return;
            }

            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            var byStable = shortcuts.ToDictionary(s => s.StableId, s => s, StringComparer.OrdinalIgnoreCase);
            var byApp = shortcuts.ToDictionary(s => s.AppId.ToString(), s => s, StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            // Check all games from this library and sync any that changed
            var ourGames = _playniteApi?.Database?.Games?.Where(g => g.PluginId == _pluginId)?.ToList() ?? new List<Game>();
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
                _logger.Info($"Writing debounced updates to shortcuts.vdf for {ourGames.Count} game(s).");
                _importExportService.WriteShortcutsWithBackup(vdfPath!, shortcuts, new ExportContext());
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process pending game updates to shortcuts.vdf");
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
                try { dir = Path.GetDirectoryName(exe) ?? sc.StartDir; } catch (Exception ex) { _logger.Warn(ex, "Failed to get directory name."); dir = sc.StartDir; }
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

    private void EnsureSteamPlayAction(Game game, SteamShortcut sc)
    {
        try
        {
            // This method is only used internally within WriteBackHandler
            // Check if steam launch is enabled and if the shortcut has an AppId
            if (sc.AppId == 0)
            {
                return;
            }

            var expectedUrl = $"{Constants.SteamRungameIdUrl}{Utils.ToShortcutGameId(sc.AppId)}";
            var current = game.GameActions?.FirstOrDefault(a => a.IsPlayAction);
            var needsUpdate = current == null || current.Type != GameActionType.URL || !string.Equals(current.Path, expectedUrl, StringComparison.OrdinalIgnoreCase);

            if (needsUpdate)
            {
                game ??= new Game();
                game.IsInstalled = true;
                var newActions = new List<GameAction>();
                
                // If steam launch is enabled, add steam URL as primary and file action as secondary
                if (true) // Assuming steam launch is enabled - this should be configurable
                {
                    newActions.Add(new GameAction
                    {
                        Name = Constants.PlaySteamActionName,
                        Type = GameActionType.URL,
                        Path = expectedUrl,
                        IsPlayAction = true
                    });
                    newActions.Add(new GameAction
                    {
                        Name = Constants.PlayDirectActionName,
                        Type = GameActionType.File,
                        Path = sc.Exe?.Trim('"'),
                        Arguments = sc.LaunchOptions,
                        WorkingDir = sc.StartDir,
                        IsPlayAction = false
                    });
                }
                
                game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(newActions);
                _playniteApi?.Database?.Games?.Update(game);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, $"Failed to ensure Steam play action for game '{game.Name}'");
        }
    }
}