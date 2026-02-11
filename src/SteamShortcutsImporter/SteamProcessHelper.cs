using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SteamShortcutsImporter;

/// <summary>
/// Utilities for detecting and managing Steam process state.
/// </summary>
internal static class SteamProcessHelper
{
    /// <summary>
    /// Checks if the Steam client is currently running.
    /// </summary>
    /// <returns>True if Steam.exe (Windows) or steam (Linux/Mac) process is running.</returns>
    public static bool IsSteamRunning()
    {
        try
        {
            // Check for common Steam process names across platforms
            var processNames = new[] { "steam", "Steam" };
            
            foreach (var name in processNames)
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Any())
                {
                    foreach (var p in processes)
                    {
                        p.Dispose();
                    }
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            // If we can't determine, assume not running (fail safe)
            Playnite.SDK.LogManager.GetLogger().Warn(ex, "Failed to check if Steam is running.");
            return false;
        }
    }

    /// <summary>
    /// Gets a user-friendly warning message about Steam being running.
    /// </summary>
    public static string GetSteamRunningWarning()
    {
        return "Steam is currently running. Writing to shortcuts.vdf while Steam is open may cause:\n\n" +
               "• Steam to overwrite your changes when it exits\n" +
               "• File lock errors preventing the write\n" +
               "• Data corruption if Steam reads the file during write\n\n" +
               "Recommendation: Close Steam completely before proceeding.\n\n" +
               "Do you want to continue anyway?";
    }

    /// <summary>
    /// Attempts to launch Steam on Windows.
    /// </summary>
    /// <param name="steamRootPath">Path to the Steam root folder.</param>
    /// <returns>True if launch was attempted successfully, false if failed or not on Windows.</returns>
    public static bool TryLaunchSteam(string? steamRootPath)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            Playnite.SDK.LogManager.GetLogger().Info("Steam launch is only supported on Windows.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(steamRootPath))
        {
            Playnite.SDK.LogManager.GetLogger().Warn("Cannot launch Steam: Steam root path is null or empty.");
            return false;
        }

        try
        {
            var steamExePath = Path.Combine(steamRootPath, "Steam.exe");
            
            if (!File.Exists(steamExePath))
            {
                Playnite.SDK.LogManager.GetLogger().Warn($"Cannot launch Steam: Steam.exe not found at {steamExePath}");
                return false;
            }

            Process.Start(steamExePath);
            Playnite.SDK.LogManager.GetLogger().Info($"Launched Steam from {steamExePath}");
            return true;
        }
        catch (Exception ex)
        {
            Playnite.SDK.LogManager.GetLogger().Error(ex, "Failed to launch Steam.");
            return false;
        }
    }
}
