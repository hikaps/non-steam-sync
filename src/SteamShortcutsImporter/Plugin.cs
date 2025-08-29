using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SteamShortcutsImporter;

public class PluginSettings : ISettings
{
    private readonly Plugin? _plugin;

    public string SteamRootPath { get; set; } = string.Empty;
    public bool LaunchViaSteam { get; set; } = true;

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
            }
            else
            {
                SteamRootPath = GuessSteamRootPath() ?? string.Empty;
                LaunchViaSteam = true;
            }
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Error(ex, "Failed to load saved settings, falling back to defaults.");
            SteamRootPath = GuessSteamRootPath() ?? string.Empty;
            LaunchViaSteam = true;
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
        Content = panel;
    }
}

public class ShortcutsLibrary : LibraryPlugin
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private const string MenuSection = "@Steam Shortcuts";

    private readonly PluginSettings settings;
    private readonly Guid pluginId = Guid.Parse("f15771cd-b6d7-4a3d-9b8e-08786a13d9c7");

    public ShortcutsLibrary(IPlayniteAPI api) : base(api)
    {
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
        yield break;
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

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
            window.Title = "Steam Shortcuts — Select Items to Import";
            window.Width = 900;
            window.Height = 650;
            window.MinWidth = 720;
            window.MinHeight = 480;

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var topBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 12, 12, 6) };
            var lblFilter = new System.Windows.Controls.TextBlock { Text = "Filter:", Margin = new System.Windows.Thickness(0, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var searchBar = new System.Windows.Controls.TextBox { Width = 320, Margin = new System.Windows.Thickness(0, 0, 16, 0) };
            var btnSelectAll = new System.Windows.Controls.Button { Content = "Select All", Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 100 };
            var btnSelectNone = new System.Windows.Controls.Button { Content = "Deselect All", MinWidth = 100 };
            topBar.Children.Add(lblFilter);
            topBar.Children.Add(searchBar);
            topBar.Children.Add(btnSelectAll);
            topBar.Children.Add(btnSelectNone);

            var listHost = new System.Windows.Controls.StackPanel();
            listHost.Children.Add(topBar);
            var listPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12, 0, 12, 0) };
            var scroll = new System.Windows.Controls.ScrollViewer { Content = listPanel, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            listHost.Children.Add(scroll);
            System.Windows.Controls.Grid.SetRow(listHost, 0);
            grid.Children.Add(listHost);

            var bottomBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 6, 12, 12), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnImport = new System.Windows.Controls.Button { Content = "Import", Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 100 };
            var btnCancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 100 };
            bottomBar.Children.Add(btnImport);
            bottomBar.Children.Add(btnCancel);
            System.Windows.Controls.Grid.SetRow(bottomBar, 1);
            grid.Children.Add(bottomBar);

            window.Content = grid;

            var checkBoxes = new List<System.Windows.Controls.CheckBox>();
            var detector = new DuplicateDetector(this);

            void RefreshList()
            {
                var filter = searchBar.Text?.Trim() ?? string.Empty;
                listPanel.Children.Clear();
                checkBoxes.Clear();
                foreach (var sc in shortcuts)
                {
                    if (!string.IsNullOrEmpty(filter) && sc.AppName?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    var summary = $"{sc.AppName} — {sc.Exe}";
                    var existsAny = detector.ExistsAnyGameMatch(sc);
                    if (existsAny)
                    {
                        summary += " [Playnite]";
                    }
                    var isAlready = !string.IsNullOrEmpty(sc.StableId) && PlayniteApi.Database.Games.Any(g => g.PluginId == Id && string.Equals(g.GameId, sc.StableId, StringComparison.OrdinalIgnoreCase));
                    var cb = new System.Windows.Controls.CheckBox
                    {
                        Content = summary,
                        IsChecked = !isAlready && !existsAny,
                        Tag = sc,
                        Margin = new System.Windows.Thickness(0, 4, 0, 4)
                    };
                    checkBoxes.Add(cb);
                    listPanel.Children.Add(cb);
                }
            }

            searchBar.TextChanged += (_, __) => RefreshList();
            RefreshList();

            btnSelectAll.Click += (_, __) => { foreach (var cb in checkBoxes) cb.IsChecked = true; };
            btnSelectNone.Click += (_, __) => { foreach (var cb in checkBoxes) cb.IsChecked = false; };
            btnCancel.Click += (_, __) => { window.DialogResult = false; window.Close(); };
            btnImport.Click += (_, __) =>
            {
                try
                {
                    var selected = checkBoxes.Where(c => c.IsChecked == true).Select(c => (SteamShortcut)c.Tag).ToList();
                    var imported = ImportShortcutsToPlaynite(selected, vdfPath!);
                    PlayniteApi.Dialogs.ShowMessage($"Imported {imported} item(s) from Steam.", Name);
                    window.DialogResult = true; window.Close();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to import selected shortcuts.");
                    PlayniteApi.Dialogs.ShowErrorMessage($"Import failed: {ex.Message}", Name);
                }
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to import from shortcuts.vdf");
            PlayniteApi.Dialogs.ShowErrorMessage($"Import failed: {ex.Message}", Name);
        }
    }

    private int ImportShortcutsToPlaynite(List<SteamShortcut> shortcuts, string vdfPath)
    {
        var existingById = PlayniteApi.Database.Games
            .Where(g => g.PluginId == Id && !string.IsNullOrEmpty(g.GameId))
            .ToDictionary(g => g.GameId, g => g, StringComparer.OrdinalIgnoreCase);

        var newGames = new List<Game>();
        foreach (var sc in shortcuts)
        {
            var id = string.IsNullOrEmpty(sc.StableId) ? sc.AppId.ToString() : sc.StableId;
            if (string.IsNullOrEmpty(id) || existingById.ContainsKey(id))
            {
                continue;
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
            if (sc.Tags?.Any() == true)
            {
                g.TagIds = new List<Guid>();
                foreach (var tagName in sc.Tags.Distinct())
                {
                    var tag = PlayniteApi.Database.Tags.Add(tagName);
                    g.TagIds.Add(tag.Id);
                }
            }
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

    private static string NormalizePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var unq = input.Trim('"');
        try { return Path.GetFullPath(unq); } catch { return unq; }
    }

    private sealed class DuplicateDetector
    {
        private readonly ShortcutsLibrary lib;
        public DuplicateDetector(ShortcutsLibrary lib) { this.lib = lib; }

        public bool ExistsAnyGameMatch(SteamShortcut sc)
        {
            try
            {
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

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
            window.Title = "Steam Shortcuts — Select Items to Export";
            window.Width = 900;
            window.Height = 650;
            window.MinWidth = 720;
            window.MinHeight = 480;

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var topBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 12, 12, 6) };
            var lblFilter = new System.Windows.Controls.TextBlock { Text = "Filter:", Margin = new System.Windows.Thickness(0, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var searchBar = new System.Windows.Controls.TextBox { Width = 320, Margin = new System.Windows.Thickness(0, 0, 16, 0) };
            var btnSelectAll = new System.Windows.Controls.Button { Content = "Select All", Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 100 };
            var btnSelectNone = new System.Windows.Controls.Button { Content = "Deselect All", MinWidth = 100 };
            topBar.Children.Add(lblFilter);
            topBar.Children.Add(searchBar);
            topBar.Children.Add(btnSelectAll);
            topBar.Children.Add(btnSelectNone);

            var listHost = new System.Windows.Controls.StackPanel();
            listHost.Children.Add(topBar);
            var listPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12, 0, 12, 0) };
            var scroll = new System.Windows.Controls.ScrollViewer { Content = listPanel, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            listHost.Children.Add(scroll);
            System.Windows.Controls.Grid.SetRow(listHost, 0);
            grid.Children.Add(listHost);

            var bottom = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 6, 12, 12), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnExport = new System.Windows.Controls.Button { Content = "Create/Update Selected", Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 150 };
            var btnCancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 100 };
            bottom.Children.Add(btnExport);
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

                foreach (var g in allGames)
                {
                    var act = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                    if (act == null || act.Type != GameActionType.File || string.IsNullOrEmpty(act.Path))
                    {
                        continue;
                    }
                    var exePath = ExpandPathVariables(g, act.Path) ?? string.Empty;
                    var name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;
                    if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    var appId = Utils.GenerateShortcutAppId(exePath, name);
                    var inSteam = existingShortcuts.ContainsKey(appId);
                    var summary = $"{name} — {exePath}" + (inSteam ? " [Steam]" : string.Empty);
                    var cb = new System.Windows.Controls.CheckBox { Content = summary, IsChecked = !inSteam, Tag = g, Margin = new System.Windows.Thickness(0, 4, 0, 4) };
                    checks.Add(cb);
                    listPanel.Children.Add(cb);
                }
            }

            searchBar.TextChanged += (_, __) => Refresh();
            Refresh();

            btnSelectAll.Click += (_, __) => { foreach (var c in checks) c.IsChecked = true; };
            btnSelectNone.Click += (_, __) => { foreach (var c in checks) c.IsChecked = false; };
            btnCancel.Click += (_, __) => { window.DialogResult = false; window.Close(); };
            btnExport.Click += (_, __) =>
            {
                try
                {
                    var selectedGames = checks.Where(c => c.IsChecked == true).Select(c => (Game)c.Tag).ToList();
                    AddGamesToSteam(selectedGames);
                    window.DialogResult = true; window.Close();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Export selection failed");
                    PlayniteApi.Dialogs.ShowErrorMessage($"Failed: {ex.Message}", Name);
                }
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Playnite→Steam sync failed");
            PlayniteApi.Dialogs.ShowErrorMessage($"Failed to sync: {ex.Message}", Name);
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

        var shortcuts = File.Exists(vdfPath) ? ShortcutsFile.Read(vdfPath!).ToList() : new List<SteamShortcut>();
        var existing = shortcuts.ToDictionary(s => s.AppId, s => s);

        int added = 0, updated = 0, skipped = 0;
        foreach (var g in games)
        {
            var action = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
            if (action == null || action.Type != GameActionType.File || string.IsNullOrEmpty(action.Path))
            {
                skipped++;
                continue;
            }

            var exePath = ExpandPathVariables(g, action.Path) ?? string.Empty;
            var workDir = ExpandPathVariables(g, action.WorkingDir);
            if (string.IsNullOrWhiteSpace(workDir) && !string.IsNullOrWhiteSpace(exePath))
            {
                try { workDir = Path.GetDirectoryName(exePath); } catch { workDir = null; }
            }
            var name = string.IsNullOrEmpty(g.Name) ? (Path.GetFileNameWithoutExtension(exePath) ?? string.Empty) : g.Name;

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

            if (g.TagIds?.Any() == true)
            {
                sc.Tags = g.TagIds
                    .Select(id => PlayniteApi.Database.Tags.Get(id)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct()
                    .ToList();
            }

            try
            {
                var gridDir = TryGetGridDirFromVdf(vdfPath!);
                if (!string.IsNullOrEmpty(gridDir))
                {
                    TryExportArtworkToGrid(g, appId, gridDir);
                }
            }
            catch (Exception aex)
            {
                Logger.Warn(aex, "Exporting artwork to grid failed.");
            }
        }

        ShortcutsFile.Write(vdfPath!, shortcuts);
        PlayniteApi.Dialogs.ShowMessage($"Updated shortcuts.vdf. +{added}/~{updated}, skipped {skipped}", Name);
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
                    Tags = new HashSet<MetadataProperty>((sc.Tags ?? Enumerable.Empty<string>()).Select(t => new MetadataNameProperty(t))),
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

                if (game.TagIds?.Any() == true)
                {
                    sc.Tags = game.TagIds
                        .Select(id => PlayniteApi.Database.Tags.Get(id)?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!)
                        .Distinct()
                        .ToList();
                }

                if (sc.AppId == 0)
                {
                    sc.AppId = Utils.GenerateShortcutAppId(sc.Exe, sc.AppName);
                }

                // Normalize play action to Steam URL when enabled
                EnsureSteamPlayAction(game, sc);

                // Export updated artwork to grid
                var gridDir = TryGetGridDirFromVdf(vdfPath!);
                TryExportArtworkToGrid(game, sc.AppId, gridDir);

                changed = true;
            }

            if (changed)
            {
                ShortcutsFile.Write(vdfPath!, shortcuts);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to sync back to shortcuts.vdf");
        }
    }
}
