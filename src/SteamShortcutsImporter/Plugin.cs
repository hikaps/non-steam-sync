
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace SteamShortcutsImporter;

public class Plugin : LibraryPlugin
{
    public readonly ShortcutsLibrary Implementation;

    public Plugin(IPlayniteAPI api) : base(api)
    {
        Implementation = new ShortcutsLibrary(api);
    }

    public override System.Guid Id => Implementation.Id;

    public override string Name => Implementation.Name;

    public override ISettings GetSettings(bool firstRunSettings) => Implementation.GetSettings(firstRunSettings);

    public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunSettings) => Implementation.GetSettingsView(firstRunSettings);

    public override System.Collections.Generic.IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args) => Implementation.GetMainMenuItems(args);

    public override System.Collections.Generic.IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args) => Implementation.GetGameMenuItems(args);

    public override System.Collections.Generic.IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args) => Implementation.GetGames(args);
}
