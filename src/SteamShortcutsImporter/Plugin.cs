using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Windows.Media;

namespace SteamShortcutsImporter;

public class PluginSettings : ISettings
{
    private readonly Plugin? _plugin;

    public string SteamRootPath { get; set; } = string.Empty;
    public bool LaunchViaSteam { get; set; } = true;
    public Dictionary<string, string> ExportMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void BeginEdit() { }
    public void CancelEdit() { }
    public void EndEdit()
    {
        if (_plugin != null)
        {
            _plugin.SavePluginSettings(this);
        }
    }

    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SteamRootPath))
        {
            errors.Add("Steam library path is required.");
        }
        return errors.Count == 0;
    }

    public PluginSettings() { }

    public PluginSettings(Plugin plugin)
    {
        _plugin = plugin;
        try
        {
            var saved = plugin.LoadPluginSettings<PluginSettings>();
            if (saved != null)
            {
                SteamRootPath = saved.SteamRootPath;
                LaunchViaSteam = saved.LaunchViaSteam;
                ExportMap = saved.ExportMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                SteamRootPath = GuessSteamRootPath() ?? string.Empty;
                LaunchViaSteam = true;
                ExportMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Error(ex, "Failed to load saved settings, falling back to defaults.");
            SteamRootPath = GuessSteamRootPath() ?? string.Empty;
            LaunchViaSteam = true;
            ExportMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? GuessSteamRootPath()
    {
        try
        {
            var regPath = Registry.CurrentUser.OpenSubKey(@"Software\\Valve\\Steam")?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(regPath) && Directory.Exists(regPath))
            {
                return regPath;
            }

            var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            var pf = Environment.GetEnvironmentVariable("ProgramFiles");
            var local = Environment.GetEnvironmentVariable("LocalAppData");

            var candidates = new[]
            {
                pf86 != null ? Path.Combine(pf86, "Steam") : null,
                pf != null ? Path.Combine(pf, "Steam") : null,
                local != null ? Path.Combine(local, "Steam") : null
            };

            foreach (var c in candidates.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                if (Directory.Exists(c!))
                {
                    return c!;
                }
            }

            var fallback1 = @"C:\\Program Files (x86)\\Steam";
            if (Directory.Exists(fallback1))
            {
                return fallback1;
            }
            var fallback2 = @"C:\\Program Files\\Steam";
            if (Directory.Exists(fallback2))
            {
                return fallback2;
            }
        }
        catch { }
        return null;
    }
}

public class PluginSettingsView : System.Windows.Controls.UserControl
{
    public PluginSettingsView()
    {
        var pathBox = new System.Windows.Controls.TextBox { Name = "SteamRootPathBox", MinWidth = 400 };
        pathBox.SetBinding(System.Windows.Controls.TextBox.TextProperty,
            new System.Windows.Data.Binding("SteamRootPath") { Mode = System.Windows.Data.BindingMode.TwoWay });

        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Steam library path (e.g., C\\\\Program Files (x86)\\\\Steam):", FontWeight = System.Windows.FontWeights.Bold, FontSize = 11 });
        pathBox.Margin = new System.Windows.Thickness(0, 4, 0, 0);
        panel.Children.Add(pathBox);
        var launchCheck = new System.Windows.Controls.CheckBox { Content = "Launch via Steam (rungameid) when possible", Margin = new System.Windows.Thickness(0,8,0,0) };
        launchCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("LaunchViaSteam") { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.Children.Add(launchCheck);
        var backupBtn = new System.Windows.Controls.Button { Content = "Open Backup Folder", Margin = new System.Windows.Thickness(0,8,0,0), MinWidth = 160 };
        backupBtn.Click += (_, __) =>
        {
            try
            {
                var libDataDir = ShortcutsLibrary.TryGetBackupRootStatic();
                if (!string.IsNullOrEmpty(libDataDir))
                {
                    System.IO.Directory.CreateDirectory(libDataDir);
                    var psi = new System.Diagnostics.ProcessStartInfo { FileName = libDataDir, UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch { }
        };
        panel.Children.Add(backupBtn);
        Content = panel;
    }
}

public class ShortcutsLibrary : LibraryPlugin
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private const string MenuSection = "@Steam Shortcuts";
    private static ShortcutsLibrary? Instance;

    private readonly PluginSettings settings;
    private readonly Guid pluginId = Guid.Parse("f15771cd-b6d7-4a3d-9b8e-08786a13d9c7");

    public ShortcutsLibrary(IPlayniteAPI api) : base(api)
    {
        Instance = this;
        try
        {
            settings = new PluginSettings(this);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize plugin settings.");
            settings = new PluginSettings(this) { SteamRootPath = string.Empty };
        }

        try
        {
            // Listen for game updates to sync back changes (write-back)
            if (PlayniteApi != null && PlayniteApi.Database != null)
            {
                PlayniteApi.Database.Games.ItemUpdated += Games_ItemUpdated;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to attach Games.ItemUpdated handler.");
        }
    }

    public override Guid Id => pluginId;

    public override string Name => "Steam Shortcuts";

    public override ISettings GetSettings(bool firstRunSettings) => settings;

    public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
    {
        var view = new PluginSettingsView { DataContext = settings };
        return view;
    }

    internal string GetBackupRootDir()
    {
        try
        {
            return Path.Combine(GetPluginUserDataPath(), "backups");
        }
        catch { return string.Empty; }
    }

    internal static string? TryGetBackupRootStatic()
    {
        return Instance?.GetBackupRootDir();
    }

    private void CreateManagedBackup(string sourceFilePath, string kind)
    {
        try
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return;
            var root = GetBackupRootDir();
            if (string.IsNullOrEmpty(root)) return;
            var dir = Path.Combine(root, kind);
            Directory.CreateDirectory(dir);

            string sourceName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string userSegment = TryGetSteamUserFromPath(sourceFilePath) ?? "user";
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupName = $"{kind}-{userSegment}-{sourceName}-{ts}.bak.vdf";
            string dst = Path.Combine(dir, backupName);
            File.Copy(sourceFilePath, dst, overwrite: true);

            // keep last 5 backups for this kind/user/sourceName
            var patternPrefix = $"{kind}-{userSegment}-{sourceName}-";
            var files = new DirectoryInfo(dir)
                .GetFiles("*.bak.vdf")
                .Where(f => f.Name.StartsWith(patternPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            for (int i = 5; i < files.Count; i++)
            {
                try { files[i].Delete(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed to create managed backup for '{sourceFilePath}'");
        }
    }

    private static string? TryGetSteamUserFromPath(string path)
    {
        try
        {
            var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int idx = Array.FindIndex(parts, p => string.Equals(p, "userdata", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < parts.Length)
            {
                return parts[idx + 1];
            }
        }
        catch { }
        return null;
    }

    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = "Steam Shortcuts: Sync Steam → Playnite…",
            MenuSection = MenuSection,
            Action = _ => { ShowImportDialog(); }
        };
        yield return new MainMenuItem
        {
            Description = "Steam Shortcuts: Sync Playnite → Steam…",
            MenuSection = MenuSection,
            Action = _ => { ShowAddToSteamDialog(); }
        };
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        if (args.Games?.Any() == true)
        {
            yield return new GameMenuItem
            {
                Description = "Add/Update in Steam",
                MenuSection = "Steam Shortcuts",
                Action = _ =>
                {
                    try { AddGamesToSteam(args.Games); }
                    catch (Exception ex) { Logger.Error(ex, "Context Add/Update in Steam failed"); }
                }
            };

            yield return new GameMenuItem
            {
                Description = "Copy Steam Launch URL",
                MenuSection = "Steam Shortcuts",
                Action = _ =>
                {
                    try
                    {
                        var g = args.Games.First();
                        var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                        string exe = string.Empty;
                        if (act?.Type == GameActionType.File && !string.IsNullOrEmpty(act.Path))
                        {
                            exe = ExpandPathVariables(g, act.Path) ?? string.Empty;
                        }
                        var appId = Utils.GenerateShortcutAppId(exe, g.Name);
                        var url = $"steam://rungameid/{Utils.ToShortcutGameId(appId)}";
                        System.Windows.Clipboard.SetText(url);
                        PlayniteApi.Dialogs.ShowMessage("Copied launch URL to clipboard.", Name);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Copy rungameid failed");
                    }
                }
            };

            yield return new GameMenuItem
            {
                Description = "Open Steam Grid Folder",
                MenuSection = "Steam Shortcuts",
                Action = _ =>
                {
                    try
                    {
                        var vdf = ResolveShortcutsVdfPath();
                        var grid = string.IsNullOrEmpty(vdf) ? null : TryGetGridDirFromVdf(vdf);
                        if (!string.IsNullOrEmpty(grid) && Directory.Exists(grid))
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo { FileName = grid, UseShellExecute = true };
                            System.Diagnostics.Process.Start(psi);
                        }
                        else
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage("Grid folder not found. Check Steam path in settings.", Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Open grid folder failed");
                    }
                }
            };
        }
    }

    private void ShowImportDialog()
    {
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid Steam library path in settings.", Name);
            return;
        }
        try
        {
            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            var detector = new DuplicateDetector(this);
            var gridDir = TryGetGridDirFromVdf(vdfPath!);

            ShowSelectionDialog(
                title: "Steam Shortcuts — Select Items to Import",
                items: shortcuts.OrderBy(s => s.AppName, StringComparer.OrdinalIgnoreCase).ToList(),
                displayText: sc =>
                {
                    var target = LooksLikeUrl(sc.LaunchOptions) ? sc.LaunchOptions : (sc.Exe ?? string.Empty).Trim('"');
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    var baseText = $"{sc.AppName} — {target}";
                    return existsAny ? baseText + " [Playnite]" : baseText;
                },
                previewImage: sc => TryPickGridPreview(sc.AppId, gridDir),
                isInitiallyChecked: sc =>
                {
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    var isAlready = !string.IsNullOrEmpty(sc.StableId) && PlayniteApi.Database.Games.Any(g => g.PluginId == Id && string.Equals(g.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                    return !isAlready && !existsAny;
                },
                isNew: sc =>
                {
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    var isAlready = !string.IsNullOrEmpty(sc.StableId) && PlayniteApi.Database.Games.Any(g => g.PluginId == Id && string.Equals(g.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                    return !isAlready && !existsAny;
                },
                confirmLabel: "Import",
                onConfirm: selected =>
                {
                    var imported = ImportShortcutsToPlaynite(selected, vdfPath!, out var skipped);
                    var msg = skipped > 0 ? $"Imported {imported} item(s). Skipped {skipped} existing item(s)." : $"Imported {imported} item(s) from Steam.";
                    PlayniteApi.Dialogs.ShowMessage(msg, Name);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to import from shortcuts.vdf");
            PlayniteApi.Dialogs.ShowErrorMessage($"Import failed: {ex.Message}", Name);
        }
    }

    private int ImportShortcutsToPlaynite(List<SteamShortcut> shortcuts, string vdfPath, out int skipped)
    {
        var existingById = PlayniteApi.Database.Games
            .Where(g => g.PluginId == Id && !string.IsNullOrEmpty(g.GameId))
            .ToDictionary(g => g.GameId, g => g, StringComparer.OrdinalIgnoreCase);

        var newGames = new List<Game>();
        var detector = new DuplicateDetector(this);
        skipped = 0;
        foreach (var sc in shortcuts)
        {
            // Skip if an equivalent game already exists in Playnite (any library)
            if (detector.ExistsAnyGameMatch(sc))
            {
                skipped++; continue;
            }
            var id = string.IsNullOrEmpty(sc.StableId) ? sc.AppId.ToString() : sc.StableId;
            if (string.IsNullOrEmpty(id) || existingById.ContainsKey(id))
            {
                skipped++; continue;
            }
            var g = new Game
            {
                PluginId = Id,
                GameId = id,
                Name = sc.AppName,
                InstallDirectory = string.IsNullOrEmpty(sc.StartDir) ? null : sc.StartDir,
                IsInstalled = true,
                GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(new[] { BuildPlayAction(sc) })
            };
            
            newGames.Add(g);
        }

        if (newGames.Count > 0)
        {
            PlayniteApi.Database.Games.Add(newGames);
            try
            {
                var gridDir = TryGetGridDirFromVdf(vdfPath);
                if (!string.IsNullOrEmpty(gridDir) && Directory.Exists(gridDir))
                {
                    foreach (var g in newGames)
                    {
                        var sc = shortcuts.FirstOrDefault(s => s.StableId == g.GameId || s.AppId.ToString() == g.GameId);
                        if (sc != null)
                        {
                            TryImportArtworkFromGrid(g, sc.AppId, gridDir!);
                        }
                    }
                }
            }
            catch (Exception aex)
            {
                Logger.Warn(aex, "Artwork import from grid failed.");
            }
        }
        return newGames.Count;
    }

    private bool ExistsAnyGameMatch(SteamShortcut sc)
    {
        var detector = new DuplicateDetector(this);
        return detector.ExistsAnyGameMatch(sc);
    }

    private sealed class DuplicateDetector
    {
        private readonly ShortcutsLibrary lib;
        public DuplicateDetector(ShortcutsLibrary lib) { this.lib = lib; }

        public bool ExistsAnyGameMatch(SteamShortcut sc)
        {
            try
            {
                // Mapping-based: if this appId was exported from an existing Playnite game, treat as duplicate
                if (sc.AppId != 0 && lib.settings.ExportMap.TryGetValue(sc.AppId.ToString(), out var pgid))
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
                var scExeNorm = DuplicateUtils.NormalizePath(sc.Exe);
                if (!string.IsNullOrEmpty(scExeNorm))
                {
                    foreach (var g in lib.PlayniteApi.Database.Games.Where(x => string.Equals(x.Name, sc.AppName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                        if (act?.Type == GameActionType.File && !string.IsNullOrEmpty(act.Path))
                        {
                            var exe = lib.ExpandPathVariables(g, act.Path) ?? string.Empty;
                            var exeNorm = DuplicateUtils.NormalizePath(exe);
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
                            if (act.Path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public Game FindLibraryGameByIds(SteamShortcut sc)
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
            catch { }
            return null;
        }
    }

    private void ShowAddToSteamDialog()
    {
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid Steam library path in settings.", Name);
            return;
        }
        try
        {
            var allGames = PlayniteApi.Database.Games.Where(g => !g.Hidden).ToList();
            var shortcuts = File.Exists(vdfPath) ? ShortcutsFile.Read(vdfPath!).ToList() : new List<SteamShortcut>();
            var existingShortcuts = shortcuts.ToDictionary(s => s.AppId, s => s);

            ShowSelectionDialog(
                title: "Steam Shortcuts — Select Items to Export",
                items: allGames.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                displayText: g =>
                {
                    var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                    if (act == null || string.IsNullOrEmpty(act.Path)) return g.Name ?? string.Empty;
                    var exePath = act.Type == GameActionType.File ? (ExpandPathVariables(g, act.Path) ?? string.Empty) : "explorer.exe";
                    var name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;
                    var appId = Utils.GenerateShortcutAppId(exePath, name);
                    var inSteam = existingShortcuts.ContainsKey(appId);
                    var target = act.Type == GameActionType.File ? exePath : act.Path;
                    return (name + " — " + target) + (inSteam ? " [Steam]" : string.Empty);
                },
                previewImage: g => TryPickPlaynitePreview(g),
                isInitiallyChecked: g =>
                {
                    var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                    if (act == null || string.IsNullOrEmpty(act.Path)) return false;
                    var exePath = act.Type == GameActionType.File ? (ExpandPathVariables(g, act.Path) ?? string.Empty) : "explorer.exe";
                    var name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;
                    var appId = Utils.GenerateShortcutAppId(exePath, name);
                    var inSteam = existingShortcuts.ContainsKey(appId);
                    return !inSteam;
                },
                isNew: g =>
                {
                    var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                    if (act == null || string.IsNullOrEmpty(act.Path)) return false;
                    var exePath = act.Type == GameActionType.File ? (ExpandPathVariables(g, act.Path) ?? string.Empty) : "explorer.exe";
                    var name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;
                    var appId = Utils.GenerateShortcutAppId(exePath, name);
                    return !existingShortcuts.ContainsKey(appId);
                },
                confirmLabel: "Create/Update Selected",
                onConfirm: selectedGames =>
                {
                    var res = AddGamesToSteamCore(selectedGames);
                    var msg = $"Steam shortcuts updated. Created: {res.Added}, Updated: {res.Updated}, Skipped: {res.Skipped}.";
                    PlayniteApi.Dialogs.ShowMessage(msg, Name);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Playnite→Steam sync failed");
            PlayniteApi.Dialogs.ShowErrorMessage($"Failed to sync: {ex.Message}", Name);
        }
    }

    private void ShowSelectionDialog<T>(string title,
        List<T> items,
        Func<T, string> displayText,
        Func<T, string?> previewImage,
        Func<T, bool> isInitiallyChecked,
        Func<T, bool> isNew,
        string confirmLabel,
        Action<List<T>> onConfirm)
    {
        var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
        window.Title = title;
        window.Width = 900;
        window.Height = 650;
        window.MinWidth = 720;
        window.MinHeight = 480;

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var topBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 12, 12, 6) };
        var lblFilter = new System.Windows.Controls.TextBlock { Text = "Filter:", Margin = new System.Windows.Thickness(0, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = Brushes.White };
        var searchBar = new System.Windows.Controls.TextBox { Width = 320, Margin = new System.Windows.Thickness(0, 0, 16, 0), Foreground = Brushes.White };
        var btnSelectAll = new System.Windows.Controls.Button { Content = "Select All", Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 100, Foreground = Brushes.White };
        var btnSelectNone = new System.Windows.Controls.Button { Content = "Deselect All", MinWidth = 100, Foreground = Brushes.White };
        var btnInvert = new System.Windows.Controls.Button { Content = "Invert", Margin = new System.Windows.Thickness(8, 0, 0, 0), MinWidth = 80, Foreground = Brushes.White };
        var cbOnlyNew = new System.Windows.Controls.CheckBox { Content = "Only new", Margin = new System.Windows.Thickness(12, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = Brushes.White };
        topBar.Children.Add(lblFilter);
        topBar.Children.Add(searchBar);
        topBar.Children.Add(btnSelectAll);
        topBar.Children.Add(btnSelectNone);
        topBar.Children.Add(btnInvert);
        topBar.Children.Add(cbOnlyNew);
        var statusText = new System.Windows.Controls.TextBlock { Margin = new System.Windows.Thickness(16, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Opacity = 1.0, Foreground = Brushes.White };
        topBar.Children.Add(statusText);

        var contentGrid = new System.Windows.Controls.Grid();
        contentGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        contentGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        contentGrid.Children.Add(topBar);
        var listPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12, 0, 12, 0) };
        var scroll = new System.Windows.Controls.ScrollViewer
        {
            Content = listPanel,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
        };
        System.Windows.Controls.Grid.SetRow(scroll, 1);
        contentGrid.Children.Add(scroll);
        System.Windows.Controls.Grid.SetRow(contentGrid, 0);
        grid.Children.Add(contentGrid);

        var bottom = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 6, 12, 12), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var btnConfirm = new System.Windows.Controls.Button { Content = confirmLabel, Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 150, Foreground = Brushes.White };
        var btnCancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 100, Foreground = Brushes.White };
        bottom.Children.Add(btnConfirm);
        bottom.Children.Add(btnCancel);
        System.Windows.Controls.Grid.SetRow(bottom, 1);
        grid.Children.Add(bottom);

        window.Content = grid;

        var checks = new List<System.Windows.Controls.CheckBox>();

        void Refresh()
        {
            var filter = searchBar.Text?.Trim() ?? string.Empty;
            listPanel.Children.Clear();
            checks.Clear();

            foreach (var it in items)
            {
                var name = displayText(it) ?? string.Empty;
                if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (cbOnlyNew.IsChecked == true && !isNew(it))
                {
                    continue;
                }
                var cb = new System.Windows.Controls.CheckBox { Content = BuildListItemWithPreview(name, previewImage(it)), IsChecked = isInitiallyChecked(it), Tag = it, Margin = new System.Windows.Thickness(0, 4, 0, 4), Foreground = Brushes.White };
                cb.Checked += (_, __) => UpdateStatus();
                cb.Unchecked += (_, __) => UpdateStatus();
                checks.Add(cb);
                listPanel.Children.Add(cb);
            }
            UpdateStatus();
        }

        searchBar.TextChanged += (_, __) => Refresh();
        cbOnlyNew.Checked += (_, __) => Refresh();
        cbOnlyNew.Unchecked += (_, __) => Refresh();
        Refresh();

        btnSelectAll.Click += (_, __) => { foreach (var c in checks) c.IsChecked = true; UpdateStatus(); };
        btnSelectNone.Click += (_, __) => { foreach (var c in checks) c.IsChecked = false; UpdateStatus(); };
        btnInvert.Click += (_, __) => { foreach (var c in checks) c.IsChecked = !(c.IsChecked == true); UpdateStatus(); };
        btnCancel.Click += (_, __) => { window.DialogResult = false; window.Close(); };
        btnConfirm.Click += (_, __) =>
        {
            try
            {
                var selected = checks.Where(c => c.IsChecked == true).Select(c => (T)c.Tag).ToList();
                onConfirm(selected);
                window.DialogResult = true; window.Close();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Selection dialog confirm failed");
                PlayniteApi.Dialogs.ShowErrorMessage($"Failed: {ex.Message}", Name);
            }
        };

        window.ShowDialog();

        void UpdateStatus()
        {
            int selected = checks.Count(c => c.IsChecked == true);
            int total = checks.Count;
            statusText.Text = $"Selected: {selected} / {total}";
        }
    }

    private void AddGamesToSteam(IEnumerable<Game> games)
    {
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid Steam library path in settings.", Name);
            return;
        }

        var res = AddGamesToSteamCore(games);
        var msg = $"Updated shortcuts.vdf. +{res.Added}/~{res.Updated}, skipped {res.Skipped}.";
        PlayniteApi.Dialogs.ShowMessage(msg, Name);
    }

    private sealed class ExportResult { public int Added; public int Updated; public int Skipped; }

    private ExportResult AddGamesToSteamCore(IEnumerable<Game> games)
    {
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            return new ExportResult();
        }

        var shortcuts = File.Exists(vdfPath) ? ShortcutsFile.Read(vdfPath!).ToList() : new List<SteamShortcut>();
        var existing = shortcuts.ToDictionary(s => s.AppId, s => s);

        int added = 0, updated = 0, skipped = 0;
        foreach (var g in games)
        {
            var action = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
            if (action == null || string.IsNullOrEmpty(action.Path))
            {
                skipped++;
                continue;
            }

            string exePath;
            string? workDir;
            string name;

            if (action.Type == GameActionType.File)
            {
                exePath = ExpandPathVariables(g, action.Path) ?? string.Empty;
                workDir = ExpandPathVariables(g, action.WorkingDir);
                if (string.IsNullOrWhiteSpace(workDir) && !string.IsNullOrWhiteSpace(exePath))
                {
                    try { workDir = Path.GetDirectoryName(exePath); } catch { workDir = null; }
                }
                name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;
            }
            else if (action.Type == GameActionType.URL)
            {
                // Use explorer.exe to launch protocol URLs via Steam shortcut
                exePath = "explorer.exe";
                workDir = null;
                name = string.IsNullOrEmpty(g.Name) ? action.Path : g.Name;
            }
            else
            {
                skipped++;
                continue;
            }

            var appId = Utils.GenerateShortcutAppId(exePath, name);
            if (!existing.TryGetValue(appId, out var sc))
            {
                sc = new SteamShortcut { AppName = name, Exe = exePath, StartDir = workDir ?? string.Empty, AppId = appId };
                shortcuts.Add(sc); added++;
            }
            else
            {
                sc.AppName = name; sc.Exe = exePath; sc.StartDir = workDir ?? sc.StartDir; updated++;
            }

            

            try
            {
                var gridDir = TryGetGridDirFromVdf(vdfPath!);
                if (!string.IsNullOrEmpty(gridDir))
                {
                    TryExportArtworkToGrid(g, appId, gridDir);
                    // Ensure Steam icon field points to exported icon if available
                    var iconPath = TryGetGridIconPath(appId, gridDir);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        sc.Icon = iconPath!;
                    }
                }
            }
            catch (Exception aex)
            {
                Logger.Warn(aex, "Exporting artwork to grid failed.");
            }


            // Update launch options per action type
            if (action.Type == GameActionType.URL)
            {
                sc.LaunchOptions = action.Path;
            }
            else if (action.Type == GameActionType.File)
            {
                sc.LaunchOptions = ExpandPathVariables(g, action.Arguments);
            }

            // Record export mapping: appId -> Playnite game Id
            try
            {
                if (g.Id != Guid.Empty)
                {
                    settings.ExportMap[appId.ToString()] = g.Id.ToString();
                    SavePluginSettings(settings);
                }
            }
            catch { }

            // Ensure Playnite game has Steam rungameid action as default
            try
            {
                if (settings.LaunchViaSteam && appId != 0)
                {
                    EnsureSteamPlayActionForExternalGame(g, appId);
                }
            }
            catch { }
        }

        WriteShortcutsWithBackup(vdfPath!, shortcuts);
        return new ExportResult { Added = added, Updated = updated, Skipped = skipped };
    }

    private static string? TryGetGridDirFromVdf(string vdfPath)
    {
        try
        {
            var cfgDir = Path.GetDirectoryName(vdfPath);
            if (string.IsNullOrEmpty(cfgDir)) return null;
            var grid = Path.Combine(cfgDir, "grid");
            return grid;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetGridIconPath(uint appId, string? gridDir)
    {
        try
        {
            if (appId == 0 || string.IsNullOrEmpty(gridDir) || !Directory.Exists(gridDir)) return null;
            var matches = Directory.GetFiles(gridDir, appId + "_icon.*");
            return matches.FirstOrDefault();
        }
        catch { return null; }
    }

    private string? ExpandPathVariables(Game game, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        try
        {
            var result = input ?? string.Empty;
            if (!string.IsNullOrEmpty(game.InstallDirectory))
            {
                result = result.Replace("{InstallDir}", game.InstallDirectory);
            }
            result = Environment.ExpandEnvironmentVariables(result);

            var unquoted = result.Trim('"');
            if (!Path.IsPathRooted(unquoted) && !string.IsNullOrEmpty(game.InstallDirectory))
            {
                try { unquoted = Path.GetFullPath(Path.Combine(game.InstallDirectory, unquoted)); } catch { }
            }
            return unquoted;
        }
        catch
        {
            return input;
        }
    }

    private void TryExportArtworkToGrid(Game game, uint appId, string? gridDir)
    {
        if (appId == 0 || string.IsNullOrEmpty(gridDir)) return;
        try
        {
            Directory.CreateDirectory(gridDir);

            void CopyIfExists(string dbPath, string targetNameBase)
            {
                if (string.IsNullOrEmpty(dbPath)) return;
                var src = PlayniteApi.Database.GetFullFilePath(dbPath);
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

    private object BuildListItemWithPreview(string text, string? imagePath)
    {
        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            try
            {
                var img = new System.Windows.Controls.Image
                {
                    Width = 48,
                    Height = 48,
                    Margin = new System.Windows.Thickness(0, 0, 8, 0),
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imagePath))
                };
                System.Windows.Controls.Grid.SetColumn(img, 0);
                grid.Children.Add(img);
            }
            catch { }
        }

        var tb = new System.Windows.Controls.TextBlock
        {
            Text = text,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = Brushes.White,
            TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
            TextWrapping = System.Windows.TextWrapping.NoWrap
        };
        System.Windows.Controls.Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        return grid;
    }

    private string? TryPickGridPreview(uint appId, string? gridDir)
    {
        try
        {
            if (appId == 0 || string.IsNullOrEmpty(gridDir) || !Directory.Exists(gridDir)) return null;
            string[] hero = Directory.GetFiles(gridDir, appId + "_hero.*");
            string[] poster = Directory.GetFiles(gridDir, appId + "p.*");
            string[] cover = Directory.GetFiles(gridDir, appId + ".*");
            string[] icon = Directory.GetFiles(gridDir, appId + "_icon.*");
            return hero.FirstOrDefault() ?? poster.FirstOrDefault() ?? cover.FirstOrDefault() ?? icon.FirstOrDefault();
        }
        catch { return null; }
    }

    private string? TryPickPlaynitePreview(Game game)
    {
        try
        {
            string? path = null;
            if (!string.IsNullOrEmpty(game.CoverImage))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.CoverImage);
            }
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(game.Icon))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.Icon);
            }
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(game.BackgroundImage))
            {
                path = PlayniteApi.Database.GetFullFilePath(game.BackgroundImage);
            }
            return File.Exists(path ?? string.Empty) ? path : null;
        }
        catch { return null; }
    }

    private static bool LooksLikeUrl(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var val = s.Trim();
        return val.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || val.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || val.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);
    }

    private void TryImportArtworkFromGrid(Game game, uint appId, string gridDir)
    {
        try
        {
            if (appId == 0 || !Directory.Exists(gridDir)) return;

            string[] hero = Directory.GetFiles(gridDir, appId + "_hero.*");
            string[] icon = Directory.GetFiles(gridDir, appId + "_icon.*");
            string[] cover = Directory.GetFiles(gridDir, appId + ".*");
            string[] poster = Directory.GetFiles(gridDir, appId + "p.*");

            string? Pick(string[] arr) => arr.FirstOrDefault();

            var bg = Pick(hero);
            var ic = Pick(icon);
            var cv = Pick(poster.Length > 0 ? poster : cover);

            if (!string.IsNullOrEmpty(bg)) game.BackgroundImage = PlayniteApi.Database.AddFile(bg, game.Id);
            if (!string.IsNullOrEmpty(ic)) game.Icon = PlayniteApi.Database.AddFile(ic, game.Id);
            if (!string.IsNullOrEmpty(cv)) game.CoverImage = PlayniteApi.Database.AddFile(cv, game.Id);

            PlayniteApi.Database.Games.Update(game);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed importing artwork from grid for appId={appId}");
        }
    }
    public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
    {
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
        {
            Logger.Warn($"shortcuts.vdf not found. SteamRootPath= '{settings.SteamRootPath}' ResolvedVdf= '{vdfPath}'");
            return Enumerable.Empty<GameMetadata>();
        }

        try
        {
            Logger.Info($"Reading shortcuts from: {vdfPath}");
            var shortcuts = ShortcutsFile.Read(vdfPath!);

            var metas = new List<GameMetadata>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sc in shortcuts)
            {
                var existing = FindExistingGameForShortcut(sc);
                if (existing == null)
                {
                    // Avoid duplicating games that already exist in other libraries
                    var detector = new DuplicateDetector(this);
                    if (detector.ExistsAnyGameMatch(sc))
                    {
                        continue;
                    }
                }
                var chosenId = existing?.GameId ?? (!string.IsNullOrEmpty(sc.StableId) ? sc.StableId : sc.AppId.ToString());
                if (string.IsNullOrEmpty(chosenId))
                {
                    chosenId = Utils.HashString($"{sc.Exe}|{sc.AppName}");
                }
                if (seenIds.Contains(chosenId))
                {
                    continue;
                }

                var meta = new GameMetadata
                {
                    Name = sc.AppName,
                    GameId = chosenId,
                    InstallDirectory = string.IsNullOrEmpty(sc.StartDir) ? null : sc.StartDir,
                    Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty("Windows") },
                    Tags = new HashSet<MetadataProperty>(),
                    Links = new List<Link>(),
                    IsInstalled = true,
                };

                meta.GameActions = new List<GameAction> { BuildPlayAction(sc) };

                metas.Add(meta);
                seenIds.Add(chosenId);
            }

            Logger.Info($"Imported {metas.Count} shortcuts as games.");
            return metas;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read shortcuts.vdf");
            return Enumerable.Empty<GameMetadata>();
        }
    }

    private Game? FindExistingGameForShortcut(SteamShortcut sc)
    {
        try
        {
            if (!string.IsNullOrEmpty(sc.StableId))
            {
                var g = PlayniteApi.Database.Games.FirstOrDefault(x => x.PluginId == Id && string.Equals(x.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                if (g != null) return g;
            }
            if (sc.AppId != 0)
            {
                var idStr = sc.AppId.ToString();
                var g = PlayniteApi.Database.Games.FirstOrDefault(x => x.PluginId == Id && string.Equals(x.GameId, idStr, StringComparison.OrdinalIgnoreCase));
                if (g != null) return g;
            }
            var byName = PlayniteApi.Database.Games.Where(x => x.PluginId == Id && string.Equals(x.Name, sc.AppName, StringComparison.OrdinalIgnoreCase));
            foreach (var g in byName)
            {
                var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                if (act != null && act.Type == GameActionType.File)
                {
                    var exe = (act.Path ?? string.Empty).Trim('"');
                    if (string.Equals(exe, (sc.Exe ?? string.Empty).Trim('"'), StringComparison.OrdinalIgnoreCase))
                    {
                        return g;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed during existing game lookup.");
        }
        return null;
    }

    private GameAction BuildPlayAction(SteamShortcut sc)
    {
        if (settings.LaunchViaSteam && sc.AppId != 0)
        {
            var gid = Utils.ToShortcutGameId(sc.AppId);
            return new GameAction
            {
                Name = "Play (Steam)",
                Type = GameActionType.URL,
                Path = $"steam://rungameid/{gid}",
                IsPlayAction = true
            };
        }
        return new GameAction
        {
            Name = "Play",
            Type = GameActionType.File,
            Path = sc.Exe?.Trim('"'),
            Arguments = sc.LaunchOptions,
            WorkingDir = sc.StartDir,
            IsPlayAction = true
        };
    }

    private void EnsureSteamPlayAction(Game game, SteamShortcut sc)
    {
        try
        {
            if (!settings.LaunchViaSteam || sc.AppId == 0)
            {
                return;
            }

            var expectedUrl = $"steam://rungameid/{Utils.ToShortcutGameId(sc.AppId)}";
            var current = game.GameActions?.FirstOrDefault(a => a.IsPlayAction);
            var needsUpdate = current == null || current.Type != GameActionType.URL || !string.Equals(current.Path, expectedUrl, StringComparison.OrdinalIgnoreCase);

            if (needsUpdate)
            {
                game.IsInstalled = true;
                game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(new[] { BuildPlayAction(sc) });
                PlayniteApi.Database.Games.Update(game);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Failed to ensure Steam play action for game '{game.Name}'");
        }
    }

    private void EnsureSteamPlayActionForExternalGame(Game game, uint appId)
    {
        if (appId == 0) return;
        var expectedUrl = $"steam://rungameid/{Utils.ToShortcutGameId(appId)}";
        var actions = game.GameActions?.ToList() ?? new List<GameAction>();

        var existingSteam = actions.FirstOrDefault(a => a.Type == GameActionType.URL && string.Equals(a.Path, expectedUrl, StringComparison.OrdinalIgnoreCase));
        if (existingSteam != null && existingSteam.IsPlayAction)
        {
            return;
        }

        var steamAction = new GameAction
        {
            Name = "Play (Steam)",
            Type = GameActionType.URL,
            Path = expectedUrl,
            IsPlayAction = true
        };

        foreach (var a in actions) a.IsPlayAction = false;
        actions.Insert(0, steamAction);

        game.IsInstalled = true;
        game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(actions);
        PlayniteApi.Database.Games.Update(game);
    }

    private string? ResolveShortcutsVdfPath()
    {
        try
        {
            var root = settings.SteamRootPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return null;
            }
            var userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata))
            {
                return null;
            }
            foreach (var userDir in Directory.EnumerateDirectories(userdata))
            {
                var cfg = Path.Combine(userDir, "config", "shortcuts.vdf");
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

    private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
    {
        // Persist changes from Playnite back to shortcuts.vdf for this library's games
        try
        {
            var vdfPath = ResolveShortcutsVdfPath();
            if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
            {
                return;
            }

            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            var byStable = shortcuts.ToDictionary(s => s.StableId, s => s, StringComparer.OrdinalIgnoreCase);
            var byApp = shortcuts.ToDictionary(s => s.AppId.ToString(), s => s, StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            foreach (var upd in e.UpdatedItems)
            {
                var game = upd.NewData;
                if (game.PluginId != Id)
                {
                    continue;
                }

                SteamShortcut sc = null;
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

                sc.AppName = game.Name;
                var act = game.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? game.GameActions?.FirstOrDefault();
                if (act != null && act.Type == GameActionType.File)
                {
                    var exe = ExpandPathVariables(game, act.Path) ?? sc.Exe;
                    var args = ExpandPathVariables(game, act.Arguments) ?? sc.LaunchOptions;
                    var dir = ExpandPathVariables(game, act.WorkingDir);
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        try { dir = Path.GetDirectoryName(exe) ?? sc.StartDir; } catch { dir = sc.StartDir; }
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

                // Export updated artwork to grid and update icon path
                var gridDir = TryGetGridDirFromVdf(vdfPath!);
                TryExportArtworkToGrid(game, sc.AppId, gridDir);
                var iconPath = TryGetGridIconPath(sc.AppId, gridDir);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    sc.Icon = iconPath!;
                }


                changed = true;
            }

            if (changed)
            {
                WriteShortcutsWithBackup(vdfPath!, shortcuts);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to sync back to shortcuts.vdf");
        }
    }

    private void WriteShortcutsWithBackup(string vdfPath, List<SteamShortcut> shortcuts)
    {
        try
        {
            CreateManagedBackup(vdfPath, "shortcuts");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Creating shortcuts.vdf backup failed");
        }

        ShortcutsFile.Write(vdfPath, shortcuts);
    }
}
