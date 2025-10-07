using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamShortcutsImporter;

internal static class GameActionUtilities
{
    public static bool EnsureSteamLaunchAction(IList<GameAction>? existingActions, string expectedUrl, out List<GameAction> updatedActions, out GameAction steamAction)
    {
        var actions = existingActions != null ? new List<GameAction>(existingActions) : new List<GameAction>();
        bool changed = false;

        var steam = actions.FirstOrDefault(a =>
            a.Type == GameActionType.URL &&
            string.Equals(a.Path, expectedUrl, StringComparison.OrdinalIgnoreCase));

        if (steam == null)
        {
            steam = new GameAction
            {
                Name = Constants.PlaySteamActionName,
                Type = GameActionType.URL,
                Path = expectedUrl
            };
            actions.Add(steam);
            changed = true;
        }
        else
        {
            if (steam.Type != GameActionType.URL)
            {
                steam.Type = GameActionType.URL;
                changed = true;
            }

            if (!string.Equals(steam.Path, expectedUrl, StringComparison.OrdinalIgnoreCase))
            {
                steam.Path = expectedUrl;
                changed = true;
            }

            if (!string.Equals(steam.Name, Constants.PlaySteamActionName, StringComparison.Ordinal))
            {
                steam.Name = Constants.PlaySteamActionName;
                changed = true;
            }
        }

        var duplicates = actions.Where(a => !ReferenceEquals(a, steam)
            && a.Type == GameActionType.URL
            && string.Equals(a.Path, expectedUrl, StringComparison.OrdinalIgnoreCase)).ToList();
        if (duplicates.Count > 0)
        {
            foreach (var dup in duplicates)
            {
                actions.Remove(dup);
            }
            changed = true;
        }

        for (int i = 0; i < actions.Count; i++)
        {
            var act = actions[i];
            if (!ReferenceEquals(act, steam) && act.IsPlayAction)
            {
                act.IsPlayAction = false;
                changed = true;
            }
        }

        if (!steam.IsPlayAction)
        {
            steam.IsPlayAction = true;
            changed = true;
        }

        if (actions.Count == 0 || !ReferenceEquals(actions.First(), steam))
        {
            actions.Remove(steam);
            actions.Insert(0, steam);
            changed = true;
        }

        steamAction = steam;
        updatedActions = actions;
        return changed;
    }
}
