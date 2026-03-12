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
        var trackingPath = @"C:\Games";
        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { fileAction }, expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Equal(Constants.PlaySteamActionName, steamAction.Name);
        Assert.Equal(GameActionType.URL, steamAction.Type);
        Assert.Equal(expectedUrl, steamAction.Path);
        Assert.True(steamAction.IsPlayAction);
        Assert.Equal(TrackingMode.Directory, steamAction.TrackingMode);
        Assert.Equal(trackingPath, steamAction.TrackingPath);
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
        var trackingPath = @"C:\Games";
        var otherAction = new GameAction { Name = "Custom", Type = GameActionType.File, Path = "custom.exe", IsPlayAction = true };

        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { otherAction, existingSteam }, expectedUrl, trackingPath, out var updated, out var steamAction);

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
        var trackingPath = @"C:\Games";
        var steamA = new GameAction { Name = "Play (Steam)", Type = GameActionType.URL, Path = expectedUrl, IsPlayAction = true };
        var steamB = new GameAction { Name = "Play (Steam)", Type = GameActionType.URL, Path = expectedUrl, IsPlayAction = false };
        var other = new GameAction { Name = "Something Else", Type = GameActionType.File, Path = "run.exe" };

        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { steamA, steamB, other }, expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Single(updated.Where(a => a.Type == GameActionType.URL && a.Path == expectedUrl));
        Assert.Equal(2, updated.Count);
        Assert.Contains(other, updated);
    }

    [Fact]
    public void KeepsOtherSteamActionsWithDifferentTargets()
    {
        var expectedUrl = $"{Constants.SteamRungameIdUrl}100200";
        var trackingPath = @"C:\Games";
        var otherSteam = new GameAction
        {
            Name = "Official Steam",
            Type = GameActionType.URL,
            Path = $"{Constants.SteamRungameIdUrl}998877",
            IsPlayAction = false
        };

        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { otherSteam }, expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Equal(2, updated.Count);
        Assert.Contains(otherSteam, updated);
        Assert.Equal(expectedUrl, steamAction.Path);
    }

    [Fact]
    public void PreservesExistingFileActionWhenAddingSteamUrl()
    {
        var fileAction = new GameAction
        {
            Name = "Play",
            Type = GameActionType.File,
            Path = "game.exe",
            Arguments = "-windowed",
            WorkingDir = @"C:\Games",
            TrackingMode = TrackingMode.Process,
            TrackingPath = @"C:\Games\game.exe",
            IsPlayAction = true
        };

        var expectedUrl = $"{Constants.SteamRungameIdUrl}555666";
        var trackingPath = @"C:\Tracking";
        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { fileAction }, expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Equal(2, updated.Count);
        Assert.Same(fileAction, updated[1]);
        Assert.Equal("Play", fileAction.Name);
        Assert.Equal(GameActionType.File, fileAction.Type);
        Assert.Equal("game.exe", fileAction.Path);
        Assert.Equal("-windowed", fileAction.Arguments);
        Assert.Equal(@"C:\Games", fileAction.WorkingDir);
        Assert.Equal(TrackingMode.Process, fileAction.TrackingMode);
        Assert.Equal(@"C:\Games\game.exe", fileAction.TrackingPath);
        Assert.False(fileAction.IsPlayAction);
        Assert.Same(steamAction, updated[0]);
    }

    [Fact]
    public void PreservesMultipleExistingActionsWhenAddingSteamUrl()
    {
        var fileAction = new GameAction { Name = "File", Type = GameActionType.File, Path = "game.exe", IsPlayAction = true };
        var urlAction = new GameAction { Name = "Help", Type = GameActionType.URL, Path = "https://example.com", IsPlayAction = false };
        var customAction = new GameAction { Name = "Custom", Type = GameActionType.Script, Path = "script.ps1", IsPlayAction = false };

        var expectedUrl = $"{Constants.SteamRungameIdUrl}777888";
        var trackingPath = @"C:\Games";
        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { fileAction, urlAction, customAction }, expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Equal(4, updated.Count);
        Assert.Contains(fileAction, updated);
        Assert.Contains(urlAction, updated);
        Assert.Contains(customAction, updated);
        Assert.Contains(steamAction, updated);
    }

    [Fact]
    public void HandlesNullGameActionsGracefully()
    {
        var expectedUrl = $"{Constants.SteamRungameIdUrl}111222";
        var trackingPath = @"C:\Games";

        var changed = GameActionUtilities.EnsureSteamLaunchAction(null, expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Single(updated);
        Assert.Same(steamAction, updated[0]);
    }

    [Fact]
    public void HandlesEmptyGameActionsGracefully()
    {
        var expectedUrl = $"{Constants.SteamRungameIdUrl}333444";
        var trackingPath = @"C:\Games";

        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction>(), expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Single(updated);
        Assert.Same(steamAction, updated[0]);
    }

    [Fact]
    public void UpdatesTrackingPathOnExistingSteamAction()
    {
        var existingSteam = new GameAction
        {
            Name = Constants.PlaySteamActionName,
            Type = GameActionType.URL,
            Path = $"{Constants.SteamRungameIdUrl}999000",
            TrackingMode = TrackingMode.Directory,
            TrackingPath = @"C:\Old",
            IsPlayAction = true
        };

        var trackingPath = @"C:\New";
        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { existingSteam }, existingSteam.Path, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Same(existingSteam, steamAction);
        Assert.Equal(trackingPath, steamAction.TrackingPath);
        Assert.Single(updated);
    }

    [Fact]
    public void DoesNotModifyNonPlayActionPropertiesOfExistingActions()
    {
        var action = new GameAction
        {
            Name = "Primary",
            Type = GameActionType.File,
            Path = "primary.exe",
            Arguments = "-fullscreen",
            WorkingDir = @"C:\Games",
            TrackingMode = TrackingMode.Process,
            TrackingPath = @"C:\Games\primary.exe",
            IsPlayAction = true
        };

        var expectedUrl = $"{Constants.SteamRungameIdUrl}444555";
        var trackingPath = @"C:\Tracking";
        var changed = GameActionUtilities.EnsureSteamLaunchAction(new List<GameAction> { action }, expectedUrl, trackingPath, out var updated, out var steamAction);

        Assert.True(changed);
        Assert.Same(action, updated[1]);
        Assert.Equal("Primary", action.Name);
        Assert.Equal(GameActionType.File, action.Type);
        Assert.Equal("primary.exe", action.Path);
        Assert.Equal("-fullscreen", action.Arguments);
        Assert.Equal(@"C:\Games", action.WorkingDir);
        Assert.Equal(TrackingMode.Process, action.TrackingMode);
        Assert.Equal(@"C:\Games\primary.exe", action.TrackingPath);
        Assert.False(action.IsPlayAction);
        Assert.Same(steamAction, updated[0]);
    }
}
