
namespace SteamShortcutsImporter;

internal static class Constants
{
    public const string PluginName = "Steam Shortcuts";
    public const string MenuSection = "@Steam Shortcuts";
    public const string GameMenuSection = "Steam Shortcuts";
    public const string SteamToPlayniteMenuDescription = "Steam Shortcuts: Sync Steam → Playnite…";
    public const string PlayniteToSteamMenuDescription = "Steam Shortcuts: Sync Playnite → Steam…";
    public const string GameMenuAddUpdateDescription = "Add/Update in Steam";
    public const string GameMenuCopyLaunchDescription = "Copy Steam Launch URL";
    public const string GameMenuOpenGridDescription = "Open Steam Grid Folder";
    public const string GridFolderNotFoundMessage = "Grid folder not found. Check Steam path in settings.";
    public const string SteamPathRequiredMessage = "Set a valid Steam library path in settings.";
    public const string CopyLaunchUrlMessage = "Copied launch URL to clipboard.";
    public const string ImportDialogTitle = "Steam Shortcuts — Select Items to Import";
    public const string ExportDialogTitle = "Steam Shortcuts — Select Items to Export";
    public const string ShortcutsKind = "shortcuts";
    public const string BackupsDirectory = "backups";
    public const string SteamRungameIdUrl = "steam://rungameid/";
    public const string BackupFileExtension = ".bak.vdf";
    public const string BackupFileSearchPattern = "*.bak.vdf";
    public const string TimestampFormat = "yyyyMMdd_HHmmss";
    public const string DefaultUserSegment = "user";
    public const string UserDataDirectory = "userdata";
    public const string ConfigDirectory = "config";
    public const string GridDirectory = "grid";
    public const string ExplorerExe = "explorer.exe";
    public const string PlayniteTag = " [Playnite]";
    public const string SteamTag = " [Steam]";
    public const string ImportConfirmLabel = "Import";
    public const string ExportConfirmLabel = "Create/Update Selected";
    public const string FilterLabel = "Filter:";
    public const string SelectAllLabel = "Select All";
    public const string DeselectAllLabel = "Deselect All";
    public const string InvertLabel = "Invert";
    public const string OnlyNewLabel = "Only new";
    public const string CancelLabel = "Cancel";
    public const string StatusTextFormat = "Selected: {0} / {1}";
    public const string PlayDirectActionName = "Play (Direct)";
    public const string PlaySteamActionName = "Play (Steam)";
    public const string WindowsPlatformName = "Windows";
    public const string OpenBackupFolderLabel = "Open Backup Folder";
    public const string SteamRootPathBoxName = "SteamRootPathBox";
    public const string PluginId = "f15771cd-b6d7-4a3d-9b8e-08786a13d9c7";
    public const string SteamRegistryPath = @"Software\Valve\Steam";
    public const string SteamPathRegistryValue = "SteamPath";
    public const string ProgramFilesX86EnvVar = "ProgramFiles(x86)";
    public const string ProgramFilesEnvVar = "ProgramFiles";
    public const string LocalAppDataEnvVar = "LocalAppData";
    public const string DefaultSteamPathX86 = @"C:\Program Files (x86)\Steam";
    public const string DefaultSteamPath = @"C:\Program Files\Steam";
    public const string ShortcutsKey = "shortcuts";
    public const string AppNameKey = "appname";
    public const string ExeKey = "exe";
    public const string StartDirKey = "StartDir";
    public const string IconKey = "icon";
    public const string ShortcutPathKey = "ShortcutPath";
    public const string LaunchOptionsKey = "LaunchOptions";
    public const string AppIdKey = "appid";
    public const string IsHiddenKey = "IsHidden";
    public const string AllowDesktopConfigKey = "AllowDesktopConfig";
    public const string AllowOverlayKey = "AllowOverlay";
    public const string OpenVRKey = "OpenVR";
    public const string TagsKey = "tags";

    // Backup/Restore UI labels
    public const string RestoreBackupLabel = "Restore Backup...";
    public const string RestoreBackupDialogTitle = "Restore Backup";
    public const string RestoreConfirmMessage = "Restore this backup?\n\nYour current shortcuts will be backed up first.";
    public const string RestoreSuccessMessage = "Backup restored successfully.";
    public const string RestoreFailedMessage = "Failed to restore backup.";
    public const string NoBackupsFoundMessage = "No backups found for this Steam user.";
    public const string SelectSteamUserTitle = "Select Steam User";
    public const string SelectSteamUserMessage = "Multiple Steam users found. Select which user's backup to restore:";
    public const string BackupFileFilter = "VDF Backup Files (*.bak.vdf)|*.bak.vdf";
    public const string BackupFilenameFormat = "shortcuts-{0}.bak.vdf";
}
