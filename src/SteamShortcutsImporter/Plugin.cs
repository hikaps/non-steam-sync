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
            MenuSection = "@Steam Shortcuts",
            Action = _ => { ShowImportDialog(); }
        };
        yield return new MainMenuItem
        {
            Description = "Steam Shortcuts: Sync Playnite → Steam…",
            MenuSection = "@Steam Shortcuts",
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
                    var gridDir = TryGetGridDirFromVdf(vdfPath!);
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

            PlayniteApi.Dialogs.ShowMessage($"Imported {newGames.Count} item(s) from Steam.", Name);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to import from shortcuts.vdf");
            PlayniteApi.Dialogs.ShowErrorMessage($"Import failed: {ex.Message}", Name);
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
            var games = PlayniteApi.Database.Games.Where(g => !g.Hidden).ToList();
            AddGamesToSteam(games);
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
}
