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
    /// Attempts to gracefully close Steam if running using the -shutdown command.
    /// Falls back to force-killing if graceful shutdown fails.
    /// </summary>
    /// <returns>True if close was attempted (gracefully or forcefully), false otherwise.</returns>
    public static bool TryCloseSteam()
    {
        try
        {
            var logger = Playnite.SDK.LogManager.GetLogger();
            
            // Find main steam.exe process (not steamwebhelper or other child processes)
            var mainSteamProcess = Process.GetProcessesByName("steam")
                .FirstOrDefault(p => p.MainModule?.FileName?.EndsWith("steam.exe", StringComparison.OrdinalIgnoreCase) == true);
            
            if (mainSteamProcess == null)
            {
                // Try capitalized variant
                mainSteamProcess = Process.GetProcessesByName("Steam")
                    .FirstOrDefault(p => p.MainModule?.FileName?.EndsWith("steam.exe", StringComparison.OrdinalIgnoreCase) == true);
            }
            
            if (mainSteamProcess == null)
            {
                logger.Info("Steam is not running, nothing to close.");
                return false;
            }

            try
            {
                // Get Steam.exe path from the running process
                string steamExePath = mainSteamProcess.MainModule?.FileName ?? string.Empty;
                
                if (string.IsNullOrWhiteSpace(steamExePath))
                {
                    logger.Warn("Could not determine Steam.exe path from process.");
                    return false;
                }

                // Step 1: Try graceful shutdown with -shutdown command
                logger.Info($"Attempting graceful shutdown via -shutdown at {steamExePath}");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = steamExePath,
                        Arguments = "-shutdown",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to execute -shutdown command, will attempt force-kill");
                }
                
                // Step 2: Poll for up to 15 seconds to see if Steam gracefully shut down
                int maxAttempts = 15;
                for (int i = 0; i < maxAttempts; i++)
                {
                    System.Threading.Thread.Sleep(1000);
                    
                    if (!IsSteamRunning())
                    {
                        logger.Info($"Steam closed successfully after {i + 1} seconds");
                        return true;
                    }
                }
                
                // Step 3: If graceful shutdown didn't work, force-kill all Steam processes
                logger.Warn("Graceful shutdown failed, force-killing Steam processes");
                var allSteamProcesses = Process.GetProcessesByName("steam")
                    .Concat(Process.GetProcessesByName("Steam"))
                    .Concat(Process.GetProcessesByName("steamwebhelper"))
                    .Concat(Process.GetProcessesByName("SteamWebHelper"))
                    .ToList();
                
                foreach (var p in allSteamProcesses)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                        logger.Info($"Force-killed Steam process {p.Id}");
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"Failed to kill Steam process {p.Id}");
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
                
                // Give the system a moment to clean up killed processes
                System.Threading.Thread.Sleep(2000);
                
                // Verify final state
                if (IsSteamRunning())
                {
                    logger.Warn("Steam processes still running even after force-kill attempt");
                }
                else
                {
                    logger.Info("All Steam processes killed successfully");
                }
                
                return true;
            }
            finally
            {
                mainSteamProcess.Dispose();
            }
        }
        catch (Exception ex)
        {
            Playnite.SDK.LogManager.GetLogger().Warn(ex, "Failed to close Steam.");
            return false;
        }
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
