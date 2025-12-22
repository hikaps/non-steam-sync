using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamShortcutsImporter;

internal sealed class DuplicateDetector
{
    private readonly ShortcutsLibrary lib;
    private readonly SteamPathResolver pathResolver;
    private readonly HashSet<string> steamRunGameUrls;

    public DuplicateDetector(ShortcutsLibrary lib, SteamPathResolver pathResolver)
    {
        this.lib = lib;
        this.pathResolver = pathResolver;
        steamRunGameUrls = BuildRunGameIndex();
    }

    private HashSet<string> BuildRunGameIndex()
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var game in lib.PlayniteApi.Database.Games)
            {
                var acts = game?.GameActions;
                if (acts == null) continue;
                foreach (var act in acts)
                {
                    if (act?.Type == GameActionType.URL && !string.IsNullOrEmpty(act.Path))
                    {
                        var path = act.Path.Trim();
                        if (path.StartsWith(Constants.SteamRungameIdUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            urls.Add(path);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Warn(ex, "Failed to build Steam rungame index.");
        }

        return urls;
    }

    public bool ExistsAnyGameMatch(SteamShortcut sc)
    {
        try
        {
            // Mapping-based: if this appId was exported from an existing Playnite game, treat as duplicate
            if (sc.AppId != 0 && lib.Settings.ExportMap.TryGetValue(sc.AppId.ToString(), out var pgid))
            {
                if (Guid.TryParse(pgid, out var gid))
                {
                    var mapped = lib.PlayniteApi.Database.Games.Get(gid);
                    if (mapped != null)
                    {
                        return true;
                    }
                }
            }

            // 0) Aggressive: if any non-shortcuts game with the same name exists, treat as duplicate
            // This prevents re-importing shortcuts for games already present from other libraries (e.g., Steam/GOG/Epic)
            var nameMatch = lib.PlayniteApi.Database.Games.FirstOrDefault(x =>
                !x.Hidden && x.PluginId != lib.Id && string.Equals(x.Name, sc.AppName, StringComparison.OrdinalIgnoreCase));
            if (nameMatch != null)
            {
                return true;
            }

            // 1) Library-level ID match (stable or appid-string)
            if (FindLibraryGameByIds(sc) != null)
            {
                return true;
            }

            // 2) Name + File path match across all games
            var scExeNorm = DuplicateUtils.GetAbsolutePath(sc.Exe);
            if (!string.IsNullOrEmpty(scExeNorm))
            {
                foreach (var g in lib.PlayniteApi.Database.Games.Where(x => string.Equals(x.Name, sc.AppName, StringComparison.OrdinalIgnoreCase)))
                {
                    var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                    if (act?.Type == GameActionType.File && !string.IsNullOrEmpty(act.Path))
                    {
                        var exe = pathResolver.ExpandPathVariables(g, act.Path) ?? string.Empty;
                        var exeNorm = DuplicateUtils.GetAbsolutePath(exe);
                        if (string.Equals(exeNorm, scExeNorm, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            // 3) Name + Steam URL rungameid match across all games
            var appId = sc.AppId != 0 ? sc.AppId : Utils.GenerateShortcutAppId(sc.Exe ?? string.Empty, sc.AppName ?? string.Empty);
            if (appId != 0)
            {
                var expectedUrl = DuplicateUtils.ExpectedRungameUrl(appId);
                if (!string.IsNullOrEmpty(expectedUrl) && steamRunGameUrls.Contains(expectedUrl))
                {
                    return true;
                }

                foreach (var g in lib.PlayniteApi.Database.Games.Where(x => string.Equals(x.Name, sc.AppName, StringComparison.OrdinalIgnoreCase)))
                {
                    var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                    if (act?.Type == GameActionType.URL && !string.IsNullOrEmpty(act.Path))
                    {
                        if (string.Equals(act.Path, expectedUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                        // Also consider official Steam app rungameid scheme for same name
                        if (act.Path.StartsWith(Constants.SteamRungameIdUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Warn(ex, "Failed to check for existing game match.");
        }

        return false;
    }

    public Game? FindLibraryGameByIds(SteamShortcut sc)
    {
        try
        {
            if (!string.IsNullOrEmpty(sc.StableId))
            {
                var g = lib.PlayniteApi.Database.Games.FirstOrDefault(x => x.PluginId == lib.Id && string.Equals(x.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                if (g != null) return g;
            }
            if (sc.AppId != 0)
            {
                var idStr = sc.AppId.ToString();
                var g = lib.PlayniteApi.Database.Games.FirstOrDefault(x => x.PluginId == lib.Id && string.Equals(x.GameId, idStr, StringComparison.OrdinalIgnoreCase));
                if (g != null) return g;
            }
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Warn(ex, "Failed to find library game by ID.");
        }
        return null;
    }
}
