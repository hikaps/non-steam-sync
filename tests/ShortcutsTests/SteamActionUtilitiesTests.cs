using Playnite.SDK.Models;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class SteamActionUtilitiesTests
{
    [Fact]
    public void AddsSteamActionWhenMissing()
    {
        var fileAction = new GameAction
        {
            Name = "Play",
            Type = GameActionType.File,
            Path = "game.exe",
            IsPlayAction = true
        };

        var expectedUrl = $"{Constants.SteamRungameIdUrl}123456";
        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { fileAction }, expectedUrl, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Equal(Constants.PlaySteamActionName, steamAction.Name);
        Assert.Equal(GameActionType.URL, steamAction.Type);
        Assert.Equal(expectedUrl, steamAction.Path);
        Assert.True(steamAction.IsPlayAction);
        Assert.Equal(2, updated.Count);
        Assert.Same(fileAction, updated[1]);
        Assert.False(fileAction.IsPlayAction);
    }

    [Fact]
    public void NormalizesExistingSteamAction()
    {
        var existingSteam = new GameAction
        {
            Name = "Launch via Steam",
            Type = GameActionType.URL,
            Path = $"{Constants.SteamRungameIdUrl}765432",
            IsPlayAction = false
        };

        var expectedUrl = existingSteam.Path;
        var otherAction = new GameAction { Name = "Custom", Type = GameActionType.File, Path = "custom.exe", IsPlayAction = true };

        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { otherAction, existingSteam }, expectedUrl, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Same(existingSteam, steamAction);
        Assert.Equal(Constants.PlaySteamActionName, steamAction.Name);
        Assert.True(steamAction.IsPlayAction);
        Assert.False(otherAction.IsPlayAction);
        Assert.Equal(2, updated.Count);
        Assert.Same(steamAction, updated[0]);
        Assert.Same(otherAction, updated[1]);
    }

    [Fact]
    public void RemovesDuplicateSteamActionsWithSamePath()
    {
        var expectedUrl = $"{Constants.SteamRungameIdUrl}246810";
        var steamA = new GameAction { Name = "Play (Steam)", Type = GameActionType.URL, Path = expectedUrl, IsPlayAction = true };
        var steamB = new GameAction { Name = "Play (Steam)", Type = GameActionType.URL, Path = expectedUrl, IsPlayAction = false };
        var other = new GameAction { Name = "Something Else", Type = GameActionType.File, Path = "run.exe" };

        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { steamA, steamB, other }, expectedUrl, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Single(updated.Where(a => a.Type == GameActionType.URL && a.Path == expectedUrl));
        Assert.Equal(2, updated.Count);
        Assert.Contains(other, updated);
    }

    [Fact]
    public void KeepsOtherSteamActionsWithDifferentTargets()
    {
        var expectedUrl = $"{Constants.SteamRungameIdUrl}100200";
        var otherSteam = new GameAction
        {
            Name = "Official Steam",
            Type = GameActionType.URL,
            Path = $"{Constants.SteamRungameIdUrl}998877",
            IsPlayAction = false
        };

        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { otherSteam }, expectedUrl, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Equal(2, updated.Count);
        Assert.Contains(otherSteam, updated);
        Assert.Equal(expectedUrl, steamAction.Path);
    }
}
