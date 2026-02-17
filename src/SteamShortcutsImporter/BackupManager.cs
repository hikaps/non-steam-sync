using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamShortcutsImporter;

internal class BackupManager
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private static BackupManager? Instance;

    public static BackupManager? InstanceStatic => Instance;
    
    private readonly ShortcutsLibrary _library;

    public BackupManager(ShortcutsLibrary library)
    {
        _library = library;
        Instance = this;
    }

    internal string GetBackupRootDir()
    {
        try
        {
            return Path.Combine(_library.GetPluginUserDataPath(), Constants.BackupsDirectory);
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

    internal void CreateManagedBackup(string sourceFilePath, string userId)
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

    internal static string? TryGetSteamUserFromPath(string path)
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

    private string? GetShortcutsVdfPathForUser(string userId)
    {
        try
        {
            var root = _library.Settings.SteamRootPath;
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

    internal void WriteShortcutsWithBackup(string vdfPath, List<SteamShortcut> shortcuts)
    {
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