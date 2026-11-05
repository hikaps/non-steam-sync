using System.Collections.Generic;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class NameMatchTests
{
    private static Dictionary<uint, SteamShortcut> Existing(params (uint id, string name)[] items)
    {
        var d = new Dictionary<uint, SteamShortcut>();
        foreach (var (id, name) in items)
        {
            d[id] = new SteamShortcut { AppId = id, AppName = name };
        }
        return d;
    }

    [Fact]
    public void FindNameMatches_IsCaseInsensitive()
    {
        var existing = Existing((1, "Portal"));

        var matches = ImportExportService.FindNameMatches("portal", existing, new Dictionary<string, uint>());

        Assert.Single(matches);
        Assert.Equal(1u, matches[0].Key);
    }

    [Fact]
    public void FindNameMatches_ExcludesShortcutsMappedToAnotherGame()
    {
        var existing = Existing((1, "Portal"), (2, "Portal"));
        var mapped = new Dictionary<string, uint> { ["game-guid"] = 1 };

        var matches = ImportExportService.FindNameMatches("Portal", existing, mapped);

        Assert.Single(matches);
        Assert.Equal(2u, matches[0].Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void FindNameMatches_EmptyName_ReturnsNone(string? name)
    {
        var existing = Existing((1, ""));

        Assert.Empty(ImportExportService.FindNameMatches(name!, existing, new Dictionary<string, uint>()));
    }
}
