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
                try
                {
if (processes.Any())
                        {
                            return true;
                        }
                }
                finally
                {
                    // Ensure array is always disposed, even if empty
                    foreach (var p in processes)
                    {
                        p.Dispose();
                    }
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
    /// Attempts to close Steam using the Windows taskkill command.
    /// </summary>
    /// <returns>True if taskkill command executed successfully and Steam is no longer running, false otherwise.</returns>
    public static bool TryCloseSteam()
    {
        try
        {
            var logger = Playnite.SDK.LogManager.GetLogger();
            
            // Check if Steam is running first
            if (!IsSteamRunning())
            {
                logger.Info("Steam is not running, nothing to close.");
                return false;
            }

            // Use Windows taskkill command to force-close Steam
            logger.Info("Closing Steam using taskkill /F /IM Steam.exe");
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/F /IM Steam.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    logger.Warn("Failed to start taskkill process");
                    return false;
                }

                process.WaitForExit(5000); // Wait up to 5 seconds
                
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                if (!string.IsNullOrWhiteSpace(output))
                {
                    logger.Info($"taskkill output: {output}");
                }
                
                if (!string.IsNullOrWhiteSpace(error))
                {
                    logger.Warn($"taskkill error: {error}");
                }
                
                logger.Info($"taskkill exit code: {process.ExitCode}");
            }
            
            // Give Windows a moment to clean up the process
            System.Threading.Thread.Sleep(2000);
            
            // Verify Steam closed
            if (IsSteamRunning())
            {
                logger.Warn("Steam still running after taskkill");
                return false;
            }
            else
            {
                logger.Info("Steam closed successfully");
                return true;
            }
        }
        catch (Exception ex)
        {
            Playnite.SDK.LogManager.GetLogger().Warn(ex, "Failed to close Steam using taskkill");
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
