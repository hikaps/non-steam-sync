using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.IO;

namespace SteamShortcutsImporter;

/// <summary>
/// Handles Steam installation path detection and shortcuts.vdf resolution.
/// </summary>
internal class SteamPathResolver
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private readonly string? _steamRootPath;

    public SteamPathResolver(string? steamRootPath)
    {
        _steamRootPath = steamRootPath;
    }

    /// <summary>
    /// Resolves the path to the shortcuts.vdf file for the current Steam installation.
    /// Searches through all user directories and returns the first valid shortcuts.vdf found.
    /// </summary>
    public string? ResolveShortcutsVdfPath()
    {
        try
        {
            var root = _steamRootPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return null;
            }

            var userdata = Path.Combine(root, Constants.UserDataDirectory);
            if (!Directory.Exists(userdata))
            {
                return null;
            }

            foreach (var userDir in Directory.EnumerateDirectories(userdata))
            {
                var cfg = Path.Combine(userDir, Constants.ConfigDirectory, "shortcuts.vdf");
                if (File.Exists(cfg))
                {
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to resolve shortcuts.vdf path.");
        }
        return null;
    }

    /// <summary>
    /// Expands path variables in a game action path.
    /// Supports {InstallDir} variable and environment variables.
    /// </summary>
    public string? ExpandPathVariables(Game game, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        try
        {
            var result = input ?? string.Empty;

            // Replace {InstallDir} with game's install directory
            if (!string.IsNullOrEmpty(game.InstallDirectory))
            {
                result = result.Replace("{InstallDir}", game.InstallDirectory);
            }

            // Expand environment variables
            result = Environment.ExpandEnvironmentVariables(result);

            // Handle relative paths
            var unquoted = result.Trim('"');
            if (!Path.IsPathRooted(unquoted) && !string.IsNullOrEmpty(game.InstallDirectory))
            {
                try
                {
                    unquoted = Path.GetFullPath(Path.Combine(game.InstallDirectory, unquoted));
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to get full path.");
                }
            }

            return unquoted;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to expand path variables.");
            return input;
        }
    }

    /// <summary>
    /// Parses a Steam rungameid URL and extracts the AppId.
    /// </summary>
    public static uint TryParseAppIdFromRungameUrl(string? url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return 0;

            var val = url!.Trim();
            const string prefix = "steam://rungameid/";

            if (!val.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 0;

            var idStr = val.Substring(prefix.Length);
            if (!ulong.TryParse(idStr, out var gid)) return 0;

            // AppId is upper 32 bits of game id for shortcuts
            return (uint)(gid >> 32);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to parse app id from rungame url.");
            return 0;
        }
    }
}
