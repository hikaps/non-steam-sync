using System;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class DuplicateUtilsTests
{
    [Theory]
    [InlineData("\"C:\\Games\\Foo.exe\"", "C:\\Games\\FOO.exe")]
    [InlineData("/home/user/game/foo", "/home/user/game/FOO")]
    public void ArePathsEqual_IgnoresQuotesAndCase(string a, string b)
    {
        Assert.True(DuplicateUtils.ArePathsEqual(a, b));
    }

    [Fact]
    public void ExpectedRungameUrl_ComposesFromAppId()
    {
        var exe = "C:\\Games\\Foo\\foo.exe";
        var name = "Foo";
        var appId = Utils.GenerateShortcutAppId(exe, name);
        var gid = Utils.ToShortcutGameId(appId);
        var expected = $"steam://rungameid/{gid}";
        Assert.Equal(expected, DuplicateUtils.ExpectedRungameUrl(appId));
    }
}

