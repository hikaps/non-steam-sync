using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class AutoSyncTests
{
    private static readonly Guid Own = Guid.Parse(Constants.PluginId);
    private static readonly Guid SteamLib = Guid.Parse(Constants.SteamLibraryPluginId);

    private static Game MakeGame(bool installed = true, bool hidden = false, Guid? pluginId = null) => new Game
    {
        Id = Guid.NewGuid(),
        Name = "G",
        IsInstalled = installed,
        Hidden = hidden,
        PluginId = pluginId ?? Guid.Empty
    };

    private static Dictionary<string, uint> NoMap() => new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Candidate_InstalledVisibleUnmapped_IsTrue()
        => Assert.True(ImportExportService.IsAutoSyncCandidate(MakeGame(), Own, NoMap()));

    [Fact]
    public void Candidate_NotInstalled_IsFalse()
        => Assert.False(ImportExportService.IsAutoSyncCandidate(MakeGame(installed: false), Own, NoMap()));

    [Fact]
    public void Candidate_Hidden_IsFalse()
        => Assert.False(ImportExportService.IsAutoSyncCandidate(MakeGame(hidden: true), Own, NoMap()));

    [Fact]
    public void Candidate_OwnPluginGame_IsFalse()
        => Assert.False(ImportExportService.IsAutoSyncCandidate(MakeGame(pluginId: Own), Own, NoMap()));

    [Fact]
    public void Candidate_SteamLibraryGame_IsFalse()
        => Assert.False(ImportExportService.IsAutoSyncCandidate(MakeGame(pluginId: SteamLib), Own, NoMap()));

    [Fact]
    public void Candidate_AlreadyMapped_IsFalse()
    {
        var g = MakeGame();
        var map = NoMap();
        map[g.Id.ToString()] = 42;
        Assert.False(ImportExportService.IsAutoSyncCandidate(g, Own, map));
    }

    private static List<string> Remove(Dictionary<string, string> map, string[] managed, params Game[] games)
        => ImportExportService.FindExportKeysToRemove(map, managed, id => games.FirstOrDefault(g => g.Id == id), Own);

    [Fact]
    public void Remove_DeletedGame_IsRemoved()
    {
        var map = new Dictionary<string, string> { ["1"] = Guid.NewGuid().ToString() };
        Assert.Equal(new[] { "1" }, Remove(map, new string[0]));
    }

    [Fact]
    public void Remove_UninstalledManaged_IsRemoved()
    {
        var g = MakeGame(installed: false);
        var map = new Dictionary<string, string> { ["1"] = g.Id.ToString() };
        Assert.Equal(new[] { "1" }, Remove(map, new[] { "1" }, g));
    }

    [Fact]
    public void Remove_UninstalledNotManaged_IsKept()
    {
        var g = MakeGame(installed: false);
        var map = new Dictionary<string, string> { ["1"] = g.Id.ToString() };
        Assert.Empty(Remove(map, new string[0], g));
    }

    [Fact]
    public void Remove_Installed_IsKept()
    {
        var g = MakeGame(installed: true);
        var map = new Dictionary<string, string> { ["1"] = g.Id.ToString() };
        Assert.Empty(Remove(map, new[] { "1" }, g));
    }

    [Fact]
    public void Remove_OwnPluginUninstalledManaged_IsKept()
    {
        var g = MakeGame(installed: false, pluginId: Own);
        var map = new Dictionary<string, string> { ["1"] = g.Id.ToString() };
        Assert.Empty(Remove(map, new[] { "1" }, g));
    }

    [Fact]
    public void Remove_NonGuidValue_IsKept()
    {
        var map = new Dictionary<string, string> { ["1"] = "not-a-guid" };
        Assert.Empty(Remove(map, new[] { "1" }));
    }

    [Fact]
    public void DeleteGridArtwork_RemovesOnlyThatAppIdsFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grid-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in new[] { "123.png", "123p.jpg", "123_hero.png", "123_icon.png", "123_logo.png", "1234.png", "123_other.png" })
            {
                File.WriteAllText(Path.Combine(dir, f), "x");
            }

            ArtworkManager.DeleteGridArtwork(dir, 123);

            var left = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "1234.png", "123_other.png" }, left);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DeleteGridArtwork_MissingDir_DoesNotThrow()
    {
        ArtworkManager.DeleteGridArtwork(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid()), 1);
        ArtworkManager.DeleteGridArtwork(null, 1);
    }
}
