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

    // Root Steam library/install path (e.g., C:\\Program Files (x86)\\Steam)
    public string SteamRootPath { get; set; } = string.Empty;
    // If true, configure Play actions to launch via Steam rungameid.
    public bool LaunchViaSteam { get; set; } = true;

    // Persisted settings copy
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

    public PluginSettings()
    {
        // Parameterless constructor required for JSON deserialization
    }

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
                // Try to prefill with a sensible default
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
            // 1) Registry value used by Steam installer
            var regPath = Registry.CurrentUser.OpenSubKey(@"Software\\Valve\\Steam")?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(regPath) && Directory.Exists(regPath))
            {
                return regPath;
            }

            // 2) Common install folders
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
        }
        catch
        {
            // ignore and return null
        }
        return null;
    }
}

public class PluginSettingsView : System.Windows.Controls.UserControl
{
    public PluginSettingsView()
    {
        // Minimal placeholder. In a real project, add XAML and proper bindings.
        var pathBox = new System.Windows.Controls.TextBox { Name = "SteamRootPathBox", MinWidth = 400 };
        pathBox.SetBinding(System.Windows.Controls.TextBox.TextProperty,
            new System.Windows.Data.Binding("SteamRootPath") { Mode = System.Windows.Data.BindingMode.TwoWay });

        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Steam library path (e.g., C\\\\Program Files (x86)\\\\Steam):" });
        panel.Children.Add(pathBox);
        var launchCheck = new System.Windows.Controls.CheckBox { Content = "Launch via Steam (rungameid) when possible" };
        launchCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("LaunchViaSteam") { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.Children.Add(launchCheck);
        Content = panel;
    }
}

public class ShortcutsLibrary : LibraryPlugin
{
    private static readonly ILogger Logger = LogManager.GetLogger();

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
            // Fallback to empty settings to avoid hard failure
            settings = new PluginSettings(this) { SteamRootPath = string.Empty };
        }

        try
        {
            // Listen for game updates to sync back changes
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

    // Use base implementation; Playnite 10 returns null by default.

    public override ISettings GetSettings(bool firstRunSettings) => settings;

    public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings)
    {
        var view = new PluginSettingsView
        {
            DataContext = settings
        };
        return view;
    }

    public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
    {
        yield return new MainMenuItem
        {
            Description = "Steam Shortcuts: Import…",
            MenuSection = "@Steam Shortcuts",
            Action = _ => { ShowImportDialog(); }
        };
        yield return new MainMenuItem
        {
            Description = "Steam Shortcuts: Add Playnite Games…",
            MenuSection = "@Steam Shortcuts",
            Action = _ => { ShowAddToSteamDialog(); }
        };
        yield return new MainMenuItem
        {
            Description = "Steam Shortcuts: Sync Back",
            MenuSection = "@Steam Shortcuts",
            Action = _ => { SyncBackAll(); }
        };
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
            foreach (var sc in shortcuts)
            {
                var meta = new GameMetadata
                {
                    Name = sc.AppName,
                    GameId = sc.StableId,
                    InstallDirectory = string.IsNullOrEmpty(sc.StartDir) ? null : sc.StartDir,
                    Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty("Windows") },
                    Tags = new HashSet<MetadataProperty>((sc.Tags ?? Enumerable.Empty<string>())
                        .Select(t => new MetadataNameProperty(t))),
                    Links = new List<Link>(),
                    IsInstalled = true,
                };

                // Configure default play action
                meta.GameActions = new List<GameAction> { BuildPlayAction(sc) };

                metas.Add(meta);
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
            Path = sc.Exe,
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

    private void ShowAddToSteamDialog()
    {
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid Steam library path in settings (we’ll find shortcuts.vdf automatically).", Name);
            return;
        }

        try
        {
            var allGames = PlayniteApi.Database.Games.Where(g => !g.Hidden).ToList();
            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
            window.Title = "Steam Shortcuts — Add Playnite Games";
            window.Width = 900;
            window.Height = 650;

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var searchBar = new System.Windows.Controls.TextBox { Margin = new System.Windows.Thickness(8), PlaceholderText = "Filter games..." };
            var listPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(8) };
            var scroll = new System.Windows.Controls.ScrollViewer { Content = listPanel, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };

            var container = new System.Windows.Controls.StackPanel();
            container.Children.Add(searchBar);
            container.Children.Add(scroll);
            System.Windows.Controls.Grid.SetRow(container, 0);
            grid.Children.Add(container);

            var bottom = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(8), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var importBtn = new System.Windows.Controls.Button { Content = "Add to Steam", Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel" };
            bottom.Children.Add(importBtn);
            bottom.Children.Add(cancelBtn);
            System.Windows.Controls.Grid.SetRow(bottom, 1);
            grid.Children.Add(bottom);

            window.Content = grid;

            // Build candidates: only games with a File play action
            var candidates = new List<(Game game, GameAction action, string summary, uint calcAppId)>();
            foreach (var g in allGames)
            {
                var action = g.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? g.GameActions?.FirstOrDefault();
                if (action == null || action.Type != GameActionType.File || string.IsNullOrEmpty(action.Path))
                {
                    continue;
                }
                var exe = action.Path;
                var name = g.Name;
                var calcApp = Utils.GenerateShortcutAppId(exe, name);
                var summary = $"{name} — {exe}";
                candidates.Add((g, action, summary, calcApp));
            }

            // Existing map to avoid duplicates
            var existing = shortcuts.ToDictionary(s => s.AppId, s => s);

            var entries = new List<System.Windows.Controls.CheckBox>();
            void RefreshList()
            {
                var filter = searchBar.Text?.Trim() ?? string.Empty;
                listPanel.Children.Clear();
                entries.Clear();
                foreach (var c in candidates)
                {
                    if (!string.IsNullOrEmpty(filter) && c.game.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    var already = existing.ContainsKey(c.calcAppId) || shortcuts.Any(s => string.Equals(s.AppName, c.game.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(s.Exe, c.action.Path, StringComparison.OrdinalIgnoreCase));
                    var cb = new System.Windows.Controls.CheckBox
                    {
                        Content = c.summary,
                        IsChecked = !already,
                        Tag = c
                    };
                    entries.Add(cb);
                    listPanel.Children.Add(cb);
                }
            }

            searchBar.TextChanged += (_, __) => RefreshList();
            RefreshList();

            cancelBtn.Click += (_, __) => { window.DialogResult = false; window.Close(); };
            importBtn.Click += (_, __) =>
            {
                try
                {
                    var selected = entries.Where(e => e.IsChecked == true).Select(e => ((Game game, GameAction action, string summary, uint calcAppId))e.Tag).ToList();
                    if (selected.Count == 0)
                    {
                        window.DialogResult = true; window.Close(); return;
                    }

                    var list = shortcuts.ToList();
                    foreach (var item in selected)
                    {
                        if (existing.ContainsKey(item.calcAppId)) continue;
                        var sc = new SteamShortcut
                        {
                            AppName = item.game.Name,
                            Exe = item.action.Path,
                            StartDir = item.action.WorkingDir ?? item.game.InstallDirectory ?? string.Empty,
                            LaunchOptions = item.action.Arguments ?? string.Empty,
                            AppId = item.calcAppId,
                            Tags = null
                        };
                        list.Add(sc);

                        // Optionally set Play action to Steam and export artwork
                        if (settings.LaunchViaSteam)
                        {
                            EnsureSteamPlayAction(item.game, sc);
                        }
                    }

                    ShortcutsFile.Write(vdfPath!, list);

                    // Export artwork
                    var gridDir = TryGetGridDirFromVdf(vdfPath!);
                    foreach (var item in selected)
                    {
                        TryExportArtworkToGrid(item.game, item.calcAppId, gridDir);
                    }

                    PlayniteApi.Dialogs.ShowMessage($"Added {selected.Count} games to Steam shortcuts.", Name);
                    window.DialogResult = true; window.Close();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed adding games to Steam shortcuts.");
                    PlayniteApi.Dialogs.ShowErrorMessage($"Failed to add games: {ex.Message}", Name);
                }
            };

            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Add to Steam dialog error");
            PlayniteApi.Dialogs.ShowErrorMessage($"Failed to open dialog: {ex.Message}", Name);
        }
    }

    private void ForceImport()
    {
        // Triggers Playnite to refresh this library by calling GetGames again.
        // Playnite refresh is managed by the host; this method mainly validates config and logs.
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid Steam library path in settings (we’ll find shortcuts.vdf automatically).", Name);
            return;
        }
        PlayniteApi.Dialogs.ShowMessage("Run Library -> Update Game Library to import.", Name);
    }

    private void ShowImportDialog()
    {
        try
        {
            var vdfPath = ResolveShortcutsVdfPath();
            if (string.IsNullOrWhiteSpace(vdfPath) || !File.Exists(vdfPath))
            {
                PlayniteApi.Dialogs.ShowErrorMessage("Set a valid Steam library path in settings (we’ll find shortcuts.vdf automatically).", Name);
                return;
            }

            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            Logger.Info($"Import dialog: loaded {shortcuts.Count} shortcuts from {vdfPath}");
            if (shortcuts.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage($"No shortcuts found in {vdfPath}. If you manually copied shortcuts.vdf, ensure it’s a binary file from Steam and not a text paste.", Name);
            }

            // Prepare UI
            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowCloseButton = true
            });
            window.Title = "Steam Shortcuts — Select Items to Import";
            window.Width = 820;
            window.Height = 600;

            // Grid layout: row 0 = list (fills), row 1 = buttons (bottom)
            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var topBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(8,8,8,0) };
            var btnSelectAll = new System.Windows.Controls.Button { Content = "Select All", Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            var btnSelectNone = new System.Windows.Controls.Button { Content = "Deselect All" };
            topBar.Children.Add(btnSelectAll);
            topBar.Children.Add(btnSelectNone);

            var listHost = new System.Windows.Controls.StackPanel();
            listHost.Children.Add(topBar);
            var scroll = new System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            var listPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(8) };
            scroll.Content = listPanel;
            listHost.Children.Add(scroll);
            System.Windows.Controls.Grid.SetRow(listHost, 0);
            grid.Children.Add(listHost);

            var bottomBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(8), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var btnImport = new System.Windows.Controls.Button { Content = "Import Selected", Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            var btnCancel = new System.Windows.Controls.Button { Content = "Cancel" };
            bottomBar.Children.Add(btnImport);
            bottomBar.Children.Add(btnCancel);
            System.Windows.Controls.Grid.SetRow(bottomBar, 1);
            grid.Children.Add(bottomBar);

            window.Content = grid;

            // Build list with checkboxes; default-check only items not already present
            var existingById = PlayniteApi.Database.Games
                .Where(g => g.PluginId == Id && !string.IsNullOrEmpty(g.GameId))
                .ToDictionary(g => g.GameId, g => g, StringComparer.OrdinalIgnoreCase);

            var checkBoxes = new List<System.Windows.Controls.CheckBox>();
            foreach (var sc in shortcuts)
            {
                var summary = $"{sc.AppName} — {sc.Exe}";
                var isAlready = !string.IsNullOrEmpty(sc.StableId) && existingById.ContainsKey(sc.StableId);
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = summary,
                    IsChecked = !isAlready,
                    Tag = sc,
                    Margin = new System.Windows.Thickness(0, 4, 0, 4)
                };
                checkBoxes.Add(cb);
                listPanel.Children.Add(cb);
            }

            btnSelectAll.Click += (_, __) =>
            {
                foreach (var cb in checkBoxes) cb.IsChecked = true;
            };
            btnSelectNone.Click += (_, __) =>
            {
                foreach (var cb in checkBoxes) cb.IsChecked = false;
            };
            btnCancel.Click += (_, __) =>
            {
                window.DialogResult = false;
                window.Close();
            };
            btnImport.Click += (_, __) =>
            {
                try
                {
                    var selected = checkBoxes
                        .Where(c => c.IsChecked == true)
                        .Select(c => (SteamShortcut)c.Tag)
                        .ToList();
                    Logger.Info($"Import dialog: user selected {selected.Count} items for import.");

                    if (selected.Count > 0)
                    {
                        var newGames = new List<Game>();
                        foreach (var sc in selected)
                        {
                            // Skip duplicates by GameId
                            if (!string.IsNullOrEmpty(sc.StableId) && existingById.ContainsKey(sc.StableId))
                            {
                                continue;
                            }

                            var g = new Game
                            {
                                PluginId = Id,
                                GameId = sc.StableId,
                                Name = sc.AppName,
                                InstallDirectory = string.IsNullOrEmpty(sc.StartDir) ? null : sc.StartDir,
                                IsInstalled = true,
                                GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(
                                    new[]
                                    {
                                        BuildPlayAction(sc)
                                    })
                            };

                            // Apply tags if any exist
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
                            // Optionally sync existing artwork from Steam grid into Playnite on import
                            try
                            {
                                var gridDir = TryGetGridDirFromVdf(vdfPath!);
                                if (!string.IsNullOrEmpty(gridDir) && Directory.Exists(gridDir))
                                {
                                    foreach (var g in newGames)
                                    {
                                        var appId = selected.First(s => s.StableId == g.GameId).AppId;
                                        TryImportArtworkFromGrid(g, appId, gridDir!);
                                    }
                                }
                            }
                            catch (Exception aex)
                            {
                                Logger.Warn(aex, "Import dialog: artwork import from grid failed.");
                            }
                            Logger.Info($"Import dialog: imported {newGames.Count} games.");
                        }
                    }

                    window.DialogResult = true;
                    window.Close();
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
            Logger.Error(ex, "Failed to show import dialog.");
            PlayniteApi.Dialogs.ShowErrorMessage($"Failed to open import dialog: {ex.Message}", Name);
        }
    }

    private void SyncBackAll()
    {
        var vdfPath = ResolveShortcutsVdfPath();
        if (string.IsNullOrWhiteSpace(vdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid Steam library path in settings.", Name);
            return;
        }

        try
        {
            Logger.Info($"SyncBackAll: resolved shortcuts.vdf at '{vdfPath}'");
            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            var games = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();
            Logger.Info($"SyncBackAll: syncing {games.Count} games to shortcuts.vdf");

            foreach (var game in games)
            {
                var sc = shortcuts.FirstOrDefault(s => s.StableId == game.GameId) ??
                         shortcuts.FirstOrDefault(s => string.Equals(s.AppName, game.Name, StringComparison.OrdinalIgnoreCase));
                if (sc == null)
                {
                    sc = new SteamShortcut { AppName = game.Name };
                    shortcuts.Add(sc);
                }

                sc.AppName = game.Name;
                var action = game.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? game.GameActions?.FirstOrDefault();
                if (action != null && action.Type == GameActionType.File)
                {
                    sc.Exe = action.Path ?? sc.Exe;
                    sc.LaunchOptions = action.Arguments ?? sc.LaunchOptions;
                    sc.StartDir = action.WorkingDir ?? sc.StartDir;
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

                // Compute AppId if missing
                if (sc.AppId == 0)
                {
                    sc.AppId = Utils.GenerateShortcutAppId(sc.Exe, sc.AppName);
                }

                // Ensure Play action in Playnite uses Steam when possible
                EnsureSteamPlayAction(game, sc);

                // Sync artwork from Playnite to Steam grid
                var gridDir = TryGetGridDirFromVdf(vdfPath!);
                TryExportArtworkToGrid(game, sc.AppId, gridDir);
            }

            ShortcutsFile.Write(vdfPath!, shortcuts);
            PlayniteApi.Dialogs.ShowMessage("Synced to shortcuts.vdf", Name);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Sync back error");
            PlayniteApi.Dialogs.ShowErrorMessage($"Failed to sync: {ex.Message}", Name);
        }
    }
    private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
    {
        // Persist changes for games belonging to this library
        try
        {
            var vdfPath = ResolveShortcutsVdfPath();
            if (string.IsNullOrWhiteSpace(vdfPath))
            {
                return;
            }

            // Load existing shortcuts
            var shortcuts = ShortcutsFile.Read(vdfPath!).ToList();
            bool changed = false;

            foreach (var upd in e.UpdatedItems)
            {
                var game = upd.NewData;
                if (game.PluginId != Id)
                {
                    continue;
                }

                // Match by our stable id or by name+exe
                var sc = shortcuts.FirstOrDefault(s => s.StableId == game.GameId) ??
                         shortcuts.FirstOrDefault(s => string.Equals(s.AppName, game.Name, StringComparison.OrdinalIgnoreCase));

                if (sc == null)
                {
                    // New entry created in Playnite under our library: add to shortcuts
                    sc = new SteamShortcut
                    {
                        AppName = game.Name
                    };
                    shortcuts.Add(sc);
                }

                // Map back common fields
                sc.AppName = game.Name;

                var action = game.GameActions?.FirstOrDefault(a => a.IsPlayAction) ?? game.GameActions?.FirstOrDefault();
                if (action != null && action.Type == GameActionType.File)
                {
                    sc.Exe = action.Path ?? sc.Exe;
                    sc.LaunchOptions = action.Arguments ?? sc.LaunchOptions;
                    sc.StartDir = action.WorkingDir ?? sc.StartDir;
                }

                // Tags
                if (game.TagIds?.Any() == true)
                {
                    sc.Tags = game.TagIds
                        .Select(id => PlayniteApi.Database.Tags.Get(id)?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!)
                        .Distinct()
                        .ToList();
                }

                // AppId
                if (sc.AppId == 0)
                {
                    sc.AppId = Utils.GenerateShortcutAppId(sc.Exe, sc.AppName);
                }

                // Ensure Play action in Playnite uses Steam when possible
                EnsureSteamPlayAction(game, sc);

                // Sync artwork to grid
                var gridDir = TryGetGridDirFromVdf(vdfPath!);
                TryExportArtworkToGrid(game, sc.AppId, gridDir);

                changed = true;
            }

            if (changed)
            {
                Logger.Info("Games_ItemUpdated: writing back updated shortcuts.vdf");
                ShortcutsFile.Write(vdfPath!, shortcuts);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to sync back to shortcuts.vdf");
        }
    }

    private string? ResolveShortcutsVdfPath()
    {
        try
        {
            var root = settings.SteamRootPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            // Backwards compatible: user pasted full path to shortcuts.vdf
            if (File.Exists(root) && string.Equals(Path.GetFileName(root), "shortcuts.vdf", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            if (!Directory.Exists(root))
            {
                return null;
            }

            // If shortcuts.vdf is placed directly under root (user copied it there), use it
            var directCandidate = Path.Combine(root, "shortcuts.vdf");
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }

            var userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata))
            {
                return null;
            }

            var candidates = Directory.GetDirectories(userdata)
                .Select(uid => Path.Combine(uid, "config", "shortcuts.vdf"))
                .Where(File.Exists)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var chosen = candidates.First().FullName;
            Logger.Info($"Resolved shortcuts.vdf candidate: {chosen} (from {candidates.Count} candidates)");
            return chosen;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to resolve shortcuts.vdf path.");
            return null;
        }
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

    private void TryExportArtworkToGrid(Game game, uint appId, string? gridDir)
    {
        if (appId == 0 || string.IsNullOrEmpty(gridDir)) return;
        try
        {
            Directory.CreateDirectory(gridDir);

            // Helper to resolve and copy
            void CopyIfExists(string dbPath, string targetNameBase)
            {
                if (string.IsNullOrEmpty(dbPath)) return;
                var src = PlayniteApi.Database.GetFullFilePath(dbPath);
                if (string.IsNullOrEmpty(src) || !File.Exists(src)) return;
                var ext = Path.GetExtension(src);
                var dst = Path.Combine(gridDir!, targetNameBase + ext);
                File.Copy(src, dst, overwrite: true);
            }

            // Cover → <appid>.png and <appid>p.png
            if (!string.IsNullOrEmpty(game.CoverImage))
            {
                CopyIfExists(game.CoverImage, appId.ToString());
                CopyIfExists(game.CoverImage, appId + "p");
            }

            // Icon → <appid>_icon.png
            if (!string.IsNullOrEmpty(game.Icon))
            {
                CopyIfExists(game.Icon, appId + "_icon");
            }

            // Background → <appid>_hero.jpg/png
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

            // Prefer hero for background
            string[] hero = Directory.GetFiles(gridDir, appId + "_hero.*");
            string[] icon = Directory.GetFiles(gridDir, appId + "_icon.*");
            string[] cover = Directory.GetFiles(gridDir, appId + ".*");
            string[] poster = Directory.GetFiles(gridDir, appId + "p.*");

            string? Pick(string[] arr) => arr.FirstOrDefault();

            var bg = Pick(hero);
            var ic = Pick(icon);
            var cv = Pick(poster.Length > 0 ? poster : cover);

            // Assign paths into Playnite DB storage
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
}
