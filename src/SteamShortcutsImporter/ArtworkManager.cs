using Playnite.SDK;
using System;
using System.IO;
using System.Linq;

namespace SteamShortcutsImporter;

/// <summary>
/// Manages artwork import/export between Playnite and Steam grid folders.
/// </summary>
internal class ArtworkManager
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private readonly IPlayniteAPI _playniteApi;

    public ArtworkManager(IPlayniteAPI playniteApi)
    {
        _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
    }

    /// <summary>
    /// Gets the grid directory path from a shortcuts.vdf path.
    /// </summary>
    public string? TryGetGridDirFromVdf(string vdfPath)
    {
        try
        {
            var cfgDir = Path.GetDirectoryName(vdfPath);
            if (string.IsNullOrEmpty(cfgDir)) return null;
            return Path.Combine(cfgDir, "grid");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to get grid dir from vdf.");
            return null;
        }
    }

    /// <summary>
    /// Exports game artwork (cover, icon, background) to Steam grid folder.
    /// </summary>
    public void TryExportArtworkToGrid(Playnite.SDK.Models.Game game, uint appId, string? gridDir)
    {
        if (appId == 0 || string.IsNullOrEmpty(gridDir)) return;

        try
        {
            Directory.CreateDirectory(gridDir);

            void CopyIfExists(string dbPath, string targetNameBase)
            {
                if (string.IsNullOrEmpty(dbPath)) return;
                var src = _playniteApi.Database.GetFullFilePath(dbPath);
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) return;
                var ext = Path.GetExtension(src);
                var dst = Path.Combine(gridDir!, targetNameBase + ext);
                File.Copy(src, dst, overwrite: true);
            }

            if (!string.IsNullOrEmpty(game.CoverImage))
            {
                CopyIfExists(game.CoverImage, appId.ToString());
                CopyIfExists(game.CoverImage, appId + "p");
            }

            if (!string.IsNullOrEmpty(game.Icon))
            {
                CopyIfExists(game.Icon, appId + "_icon");
            }

            if (!string.IsNullOrEmpty(game.BackgroundImage))
            {
                CopyIfExists(game.BackgroundImage, appId + "_hero");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed exporting artwork to grid for appId={appId}");
        }
    }

    /// <summary>
    /// Imports artwork from Steam grid folder to Playnite game.
    /// </summary>
    public void TryImportArtworkFromGrid(Playnite.SDK.Models.Game game, uint appId, string gridDir)
    {
        try
        {
            if (appId == 0 || !Directory.Exists(gridDir)) return;

            string[] hero = Directory.GetFiles(gridDir, appId + "_hero.*", SearchOption.TopDirectoryOnly);
            string[] icon = Directory.GetFiles(gridDir, appId + "_icon.*", SearchOption.TopDirectoryOnly);
            string[] cover = Directory.GetFiles(gridDir, appId + ".*", SearchOption.TopDirectoryOnly);
            string[] poster = Directory.GetFiles(gridDir, appId + "p.*", SearchOption.TopDirectoryOnly);

            string? Pick(string[] arr) => arr.FirstOrDefault();

            var bg = Pick(hero);
            var ic = Pick(icon);
            var cv = Pick(poster.Length > 0 ? poster : cover);

            if (!string.IsNullOrEmpty(bg)) game.BackgroundImage = _playniteApi.Database.AddFile(bg, game.Id);
            if (!string.IsNullOrEmpty(ic)) game.Icon = _playniteApi.Database.AddFile(ic, game.Id);
            if (!string.IsNullOrEmpty(cv)) game.CoverImage = _playniteApi.Database.AddFile(cv, game.Id);

            _playniteApi.Database.Games.Update(game);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed importing artwork from grid for appId={appId}");
        }
    }

    /// <summary>
    /// Finds the best preview image from Steam grid for a given appId.
    /// Priority: hero > poster > cover > icon
    /// </summary>
    public string? TryPickGridPreview(uint appId, string? gridDir)
    {
        try
        {
            if (appId == 0 || string.IsNullOrEmpty(gridDir) || !Directory.Exists(gridDir)) return null;

            string[] hero = Directory.GetFiles(gridDir, appId + "_hero.*", SearchOption.TopDirectoryOnly);
            string[] poster = Directory.GetFiles(gridDir, appId + "p.*", SearchOption.TopDirectoryOnly);
            string[] cover = Directory.GetFiles(gridDir, appId + ".*", SearchOption.TopDirectoryOnly);
            string[] icon = Directory.GetFiles(gridDir, appId + "_icon.*", SearchOption.TopDirectoryOnly);

            return hero.FirstOrDefault() ?? poster.FirstOrDefault() ?? cover.FirstOrDefault() ?? icon.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to pick grid preview.");
            return null;
        }
    }

    /// <summary>
    /// Finds the best preview image from a Playnite game.
    /// Priority: cover > icon > background
    /// </summary>
    public string? TryPickPlaynitePreview(Playnite.SDK.Models.Game game)
    {
        try
        {
            string? path = null;
            if (!string.IsNullOrEmpty(game.CoverImage))
            {
                path = _playniteApi.Database.GetFullFilePath(game.CoverImage);
            }
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(game.Icon))
            {
                path = _playniteApi.Database.GetFullFilePath(game.Icon);
            }
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(game.BackgroundImage))
            {
                path = _playniteApi.Database.GetFullFilePath(game.BackgroundImage);
            }
            return File.Exists(path ?? string.Empty) ? path : null;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to pick playnite preview.");
            return null;
        }
    }

    /// <summary>
    /// Gets the icon file path for a given appId from Steam grid.
    /// </summary>
    public string? TryGetGridIconPath(uint appId, string? gridDir)
    {
        try
        {
            if (appId == 0 || string.IsNullOrEmpty(gridDir) || !Directory.Exists(gridDir)) return null;
            var matches = Directory.GetFiles(gridDir, appId + "_icon.*", SearchOption.TopDirectoryOnly);
            return matches.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to get grid icon path.");
            return null;
        }
    }
}
