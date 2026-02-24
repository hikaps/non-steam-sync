using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
    /// Resolves the path to shortcuts.vdf for a specific Steam user.
    /// If userId is null or empty, falls back to the first valid shortcuts.vdf found.
    /// </summary>
    /// <param name="userId">The Steam user ID (numeric folder name in userdata).</param>
    /// <returns>Path to shortcuts.vdf, or null if not found.</returns>
    public string? ResolveShortcutsVdfPathForUser(string? userId)
    {
        // If no specific user, use default behavior (first found)
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ResolveShortcutsVdfPath();
        }

        try
        {
            var root = _steamRootPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return null;
            }

            var vdfPath = Path.Combine(root, Constants.UserDataDirectory, userId, Constants.ConfigDirectory, "shortcuts.vdf");
            if (File.Exists(vdfPath))
            {
                return vdfPath;
            }

            // User's shortcuts.vdf doesn't exist yet - return the path anyway
            // (it will be created on export)
            var userDir = Path.Combine(root, Constants.UserDataDirectory, userId);
            if (Directory.Exists(userDir))
            {
                return vdfPath;
            }

            // User directory doesn't exist - fall back to auto-detect
            Logger.Warn($"Selected user directory not found: {userDir}. Falling back to auto-detect.");
            return ResolveShortcutsVdfPath();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to resolve shortcuts.vdf path for user {userId}.");
            return ResolveShortcutsVdfPath();
        }
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

    #region Executable Discovery

    /// <summary>
    /// Patterns for executables that are typically not the main game executable.
    /// </summary>
    private static readonly string[] ExcludedExePatterns =
    {
        "unins*", "uninst*", "uninstall*",
        "crash*", "crashreport*", "crashhandler*",
        "update*", "updater*",
        "setup*", "install*",
        "launcher*", "launch*",
        "UnityCrashHandler*", "UE4PrereqSetup*", "UEPrereqSetup*",
        "vcredist*", "vc_redist*",
        "dxsetup*", "dxwebsetup*",
        "dotnet*", "directx*",
        "redist*", "prereq*",
        "7z*", "rar*", "zip*",
        "notepad*", "cmd*", "powershell*"
    };

    /// <summary>
    /// Attempts to discover the game executable from the install directory.
    /// Tries GOG manifest first, then scans for executables.
    /// </summary>
    /// <param name="game">The game to find an executable for.</param>
    /// <returns>The path to the executable, or null if not found or ambiguous.</returns>
    public string? TryDiscoverExecutable(Game game)
    {
        if (string.IsNullOrEmpty(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
        {
            Logger.Debug($"Cannot discover exe for '{game.Name}': InstallDirectory is empty or doesn't exist.");
            return null;
        }

        try
        {
            // 1. Try GOG manifest first (most reliable for GOG games)
            var gogExe = TryParseGogManifest(game.InstallDirectory, game.GameId);
            if (!string.IsNullOrEmpty(gogExe) && File.Exists(gogExe))
            {
                Logger.Info($"Found exe via GOG manifest for '{game.Name}': {gogExe}");
                return gogExe;
            }

            // 2. Scan for executables
            var candidates = ScanForExecutables(game.InstallDirectory, maxDepth: 2);
            if (candidates.Count == 0)
            {
                Logger.Debug($"No executable candidates found for '{game.Name}' in {game.InstallDirectory}");
                return null;
            }

            // 3. Single candidate - use it
            if (candidates.Count == 1)
            {
                Logger.Info($"Auto-selected single exe for '{game.Name}': {candidates[0]}");
                return candidates[0];
            }

            // 4. Try to find a clear match by game name
            var bestMatch = SelectBestExecutable(candidates, game.Name, game.InstallDirectory);
            if (bestMatch != null)
            {
                Logger.Info($"Auto-selected best match for '{game.Name}': {bestMatch}");
                return bestMatch;
            }

            // 5. Ambiguous - return null to trigger browse dialog
            Logger.Debug($"Multiple ambiguous exe candidates ({candidates.Count}) found for '{game.Name}', manual selection required.");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error discovering executable for '{game.Name}'");
            return null;
        }
    }

    /// <summary>
    /// Parses a GOG goggame-{id}.info manifest file to extract the executable path.
    /// </summary>
    public string? TryParseGogManifest(string installDir, string? gameId)
    {
        try
        {
            // Try to find manifest by gameId first
            if (!string.IsNullOrEmpty(gameId))
            {
                var manifestPath = Path.Combine(installDir, $"goggame-{gameId}.info");
                var exePath = ParseGogManifestFile(manifestPath, installDir);
                if (exePath != null) return exePath;
            }

            // Fallback: find any goggame-*.info file
            var manifestFiles = Directory.GetFiles(installDir, "goggame-*.info", SearchOption.TopDirectoryOnly);
            foreach (var manifestPath in manifestFiles)
            {
                var exePath = ParseGogManifestFile(manifestPath, installDir);
                if (exePath != null) return exePath;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to parse GOG manifest in {installDir}: {ex.Message}");
        }

        return null;
    }

    private static string? ParseGogManifestFile(string manifestPath, string installDir)
    {
        if (!File.Exists(manifestPath)) return null;

        try
        {
            var json = File.ReadAllText(manifestPath);

            // Simple regex extraction for "path" in playTasks
            // Format: "playTasks": [{ ... "path": "game.exe" ... }]
            var pathMatch = Regex.Match(json, @"""playTasks""\s*:\s*\[\s*\{[^}]*""path""\s*:\s*""([^""]+)""", RegexOptions.Singleline);
            if (pathMatch.Success)
            {
                var relativePath = pathMatch.Groups[1].Value.Replace("\\\\", "\\");
                var fullPath = Path.Combine(installDir, relativePath);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to parse GOG manifest {manifestPath}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Scans a directory for executable files, filtering out common non-game executables.
    /// </summary>
    /// <param name="installDir">The directory to scan.</param>
    /// <param name="maxDepth">Maximum directory depth to scan (1 = root only, 2 = root + immediate subdirs).</param>
    /// <returns>List of executable paths that might be the game executable.</returns>
    public List<string> ScanForExecutables(string installDir, int maxDepth = 2)
    {
        var results = new List<string>();

        try
        {
            ScanDirectoryForExes(installDir, results, currentDepth: 1, maxDepth);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Error scanning for executables in {installDir}");
        }

        return results;
    }

    private void ScanDirectoryForExes(string directory, List<string> results, int currentDepth, int maxDepth)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (!IsExcludedExecutable(fileName))
                {
                    results.Add(file);
                }
            }

            if (currentDepth < maxDepth)
            {
                foreach (var subDir in Directory.EnumerateDirectories(directory))
                {
                    var dirName = Path.GetFileName(subDir);
                    // Skip common non-game directories
                    if (!IsExcludedDirectory(dirName))
                    {
                        ScanDirectoryForExes(subDir, results, currentDepth + 1, maxDepth);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (Exception ex)
        {
            Logger.Debug($"Error scanning directory {directory}: {ex.Message}");
        }
    }

    private static bool IsExcludedExecutable(string fileName)
    {
        foreach (var pattern in ExcludedExePatterns)
        {
            if (MatchesWildcard(fileName, pattern))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsExcludedDirectory(string dirName)
    {
        var excluded = new[] { "redist", "redistributables", "_commonredist", "support", "directx", "vcredist", "__installer", "mono", "dotnet" };
        return excluded.Any(e => dirName.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesWildcard(string input, string pattern)
    {
        // Simple wildcard matching (only supports * at end)
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern.Substring(0, pattern.Length - 1);
            return input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return input.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Selects the best executable from a list of candidates based on heuristics.
    /// </summary>
    /// <param name="candidates">List of executable paths.</param>
    /// <param name="gameName">The game name to match against.</param>
    /// <param name="installDir">The install directory (to prefer root-level exes).</param>
    /// <returns>The best match, or null if no clear winner.</returns>
    public string? SelectBestExecutable(List<string> candidates, string gameName, string installDir)
    {
        if (candidates.Count == 0) return null;

        // Normalize game name for comparison
        var normalizedGameName = NormalizeForComparison(gameName);

        // Score each candidate
        var scored = candidates
            .Select(path => new { Path = path, Score = ScoreExecutable(path, normalizedGameName, installDir) })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Only return if there's a clear winner (score >= 50 and significantly better than second)
        if (scored[0].Score >= 50)
        {
            if (scored.Count == 1 || scored[0].Score > scored[1].Score + 20)
            {
                return scored[0].Path;
            }
        }

        return null;
    }

    private static int ScoreExecutable(string path, string normalizedGameName, string installDir)
    {
        int score = 0;
        var fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        var normalizedFileName = NormalizeForComparison(fileName);

        // Exact name match: +100 points
        if (normalizedFileName.Equals(normalizedGameName, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        // Name contains game name: +50 points
        else if (normalizedFileName.Contains(normalizedGameName) || normalizedGameName.Contains(normalizedFileName))
        {
            score += 50;
        }

        // In root directory: +30 points
        var fileDir = Path.GetDirectoryName(path);
        if (fileDir != null && fileDir.Equals(installDir, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        // Larger file size (likely main exe): +10 points if > 10MB
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > 10 * 1024 * 1024) // > 10 MB
            {
                score += 10;
            }
            if (fileInfo.Length > 50 * 1024 * 1024) // > 50 MB
            {
                score += 10;
            }
        }
        catch { /* Ignore file access errors */ }

        return score;
    }

    private static string NormalizeForComparison(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Remove common suffixes, special chars, and normalize
        var result = input
            .Replace("-", "")
            .Replace("_", "")
            .Replace(".", "")
            .Replace(" ", "")
            .Replace("'", "")
            .Replace(":", "")
            .ToLowerInvariant();

        // Remove common suffixes
        var suffixes = new[] { "win64", "win32", "x64", "x86", "game", "bin", "shipping" };
        foreach (var suffix in suffixes)
        {
            if (result.EndsWith(suffix))
            {
                result = result.Substring(0, result.Length - suffix.Length);
            }
        }

        return result;
    }

    #endregion
}
