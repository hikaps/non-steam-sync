using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamShortcutsImporter;

public class PluginSettings : ISettings
{
    private readonly Plugin _plugin;

    public string ShortcutsVdfPath { get; set; } = string.Empty;

    // Persisted settings copy
    public void BeginEdit() { }
    public void CancelEdit() { }
    public void EndEdit()
    {
        _plugin.SavePluginSettings(this);
    }
    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ShortcutsVdfPath))
        {
            errors.Add("Shortcuts.vdf path is required.");
        }
        return errors.Count == 0;
    }

    public PluginSettings(Plugin plugin)
    {
        _plugin = plugin;
        var saved = plugin.LoadPluginSettings<PluginSettings>();
        if (saved != null)
        {
            ShortcutsVdfPath = saved.ShortcutsVdfPath;
        }
    }
}

public class PluginSettingsView : Playnite.SDK.Controls.PluginUserControl
{
    public PluginSettingsView()
    {
        // Minimal placeholder. In a real project, add XAML and proper bindings.
        var pathBox = new System.Windows.Controls.TextBox { Name = "ShortcutsPathBox", MinWidth = 300 };
        pathBox.SetBinding(System.Windows.Controls.TextBox.TextProperty,
            new System.Windows.Data.Binding("ShortcutsVdfPath") { Mode = System.Windows.Data.BindingMode.TwoWay });

        Content = new System.Windows.Controls.StackPanel
        {
            Children =
            {
                new System.Windows.Controls.TextBlock { Text = "Path to shortcuts.vdf:" },
                pathBox
            }
        };
    }
}

public class ShortcutsLibrary : LibraryPlugin
{
    private static readonly ILogger Logger = LogManager.GetLogger();

    private readonly PluginSettings settings;
    private readonly Guid pluginId = Guid.Parse("f15771cd-b6d7-4a3d-9b8e-08786a13d9c7");

    public ShortcutsLibrary(IPlayniteAPI api) : base(api)
    {
        settings = new PluginSettings(this);
        // Listen for game updates to sync back changes
        PlayniteApi.Database.Games.ItemUpdated += Games_ItemUpdated;
    }

    public override Guid Id => pluginId;

    public override string Name => "Steam Shortcuts";

    public override LibraryClient Client => null;

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
            Description = "Steam Shortcuts: Import",
            MenuSection = "@Steam Shortcuts",
            Action = _ => { ForceImport(); }
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
        if (string.IsNullOrWhiteSpace(settings.ShortcutsVdfPath) || !File.Exists(settings.ShortcutsVdfPath))
        {
            Logger.Warn($"shortcuts.vdf not set or missing: {settings.ShortcutsVdfPath}");
            return Enumerable.Empty<GameMetadata>();
        }

        try
        {
            var shortcuts = ShortcutsFile.Read(settings.ShortcutsVdfPath);

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
                    Links = new List<Link>()
                };

                // Configure default play action
                meta.GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Play",
                        Type = GameActionType.File,
                        Path = sc.Exe,
                        Arguments = sc.LaunchOptions,
                        WorkingDir = sc.StartDir,
                        IsPlayAction = true
                    }
                };

                metas.Add(meta);
            }

            return metas;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read shortcuts.vdf");
            return Enumerable.Empty<GameMetadata>();
        }
    }

    private void ForceImport()
    {
        // Triggers Playnite to refresh this library by calling GetGames again.
        // Playnite refresh is managed by the host; this method mainly validates config and logs.
        if (string.IsNullOrWhiteSpace(settings.ShortcutsVdfPath) || !File.Exists(settings.ShortcutsVdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid shortcuts.vdf path in settings.", Name);
            return;
        }
        PlayniteApi.Dialogs.ShowMessage("Run Library -> Update Game Library to import.", Name);
    }

    private void SyncBackAll()
    {
        if (string.IsNullOrWhiteSpace(settings.ShortcutsVdfPath))
        {
            PlayniteApi.Dialogs.ShowErrorMessage("Set a valid shortcuts.vdf path in settings.", Name);
            return;
        }

        try
        {
            var shortcuts = ShortcutsFile.Read(settings.ShortcutsVdfPath).ToList();
            var games = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();

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
                if (action != null)
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
            }

            ShortcutsFile.Write(settings.ShortcutsVdfPath, shortcuts);
            PlayniteApi.Dialogs.ShowMessage("Synced to shortcuts.vdf", Name);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Sync back error");
            PlayniteApi.Dialogs.ShowErrorMessage($"Failed to sync: {ex.Message}", Name);
        }
    }
    private void Games_ItemUpdated(object? sender, ItemUpdatedEventArgs<Game> e)
    {
        // Persist changes for games belonging to this library
        try
        {
            if (string.IsNullOrWhiteSpace(settings.ShortcutsVdfPath))
            {
                return;
            }

            // Load existing shortcuts
            var shortcuts = ShortcutsFile.Read(settings.ShortcutsVdfPath).ToList();
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
                if (action != null)
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

                changed = true;
            }

            if (changed)
            {
                ShortcutsFile.Write(settings.ShortcutsVdfPath, shortcuts);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to sync back to shortcuts.vdf");
        }
    }
}
