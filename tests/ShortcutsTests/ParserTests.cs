using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class ParserTests
{
    [Fact]
    public void RoundTripsSampleVdf()
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            "shortcuts_" + Guid.NewGuid().ToString("N") + ".vdf");

        try
        {
            var sample = new[]
            {
                new SteamShortcut
                {
                    AppName = "Sample Game",
                    Exe = "C:/Games/Sample/Sample.exe",
                    StartDir = "C:/Games/Sample",
                    LaunchOptions = "-windowed",
                    Tags = new System.Collections.Generic.List<string>{"Action"}
                }
            };

            ShortcutsFile.Write(tmp, sample);
            Assert.True(File.Exists(tmp));

            var items = ShortcutsFile.Read(tmp).ToList();
            Assert.True(items.Count == 1);

            var first = items.First();
            Assert.Equal("Sample Game", first.AppName);
            Assert.Contains("Sample.exe", first.Exe);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    [Fact]
    public void StableId_ConsistentAcrossQuotedAndUnquotedPaths()
    {
        var shortcut1 = new SteamShortcut
        {
            AppName = "Test Game",
            Exe = "C:\\Games\\Test\\game.exe"
        };

        var shortcut2 = new SteamShortcut
        {
            AppName = "Test Game",
            Exe = "\"C:\\Games\\Test\\game.exe\""
        };

        Assert.Equal(shortcut1.StableId, shortcut2.StableId);
    }

    [Fact]
    public void StableId_DifferentForDifferentGames()
    {
        var shortcut1 = new SteamShortcut
        {
            AppName = "Game 1",
            Exe = "game1.exe"
        };

        var shortcut2 = new SteamShortcut
        {
            AppName = "Game 2",
            Exe = "game2.exe"
        };

        Assert.NotEqual(shortcut1.StableId, shortcut2.StableId);
    }

    [Fact]
    public void StableId_TrimsWhitespaceInName()
    {
        var shortcut1 = new SteamShortcut
        {
            AppName = "Test Game",
            Exe = "game.exe"
        };

        var shortcut2 = new SteamShortcut
        {
            AppName = "  Test Game  ",
            Exe = "game.exe"
        };

        Assert.Equal(shortcut1.StableId, shortcut2.StableId);
    }
}
