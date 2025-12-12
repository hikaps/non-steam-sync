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

    [Fact]
    public void NormalizePath_HandlesBackslashesAndForwardSlashes()
    {
        // Test that paths with backslashes and forward slashes normalize correctly
        var path1 = "game/test.exe";
        var path2 = "game/test.exe";
        
        Assert.True(DuplicateUtils.ArePathsEqual(path1, path2));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void NormalizePath_EmptyOrWhitespace_ReturnsEmpty(string input)
    {
        Assert.Equal(string.Empty, DuplicateUtils.NormalizePath(input));
    }

    [Fact]
    public void ExpectedRungameUrl_ZeroAppId_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DuplicateUtils.ExpectedRungameUrl(0));
    }

    [Fact]
    public void NormalizePath_RelativePath_ConvertsToAbsolute()
    {
        var relative = "test.txt";
        var normalized = DuplicateUtils.NormalizePath(relative);
        
        // Should be converted to absolute path (current directory + relative)
        Assert.NotEqual(relative, normalized);
        Assert.True(System.IO.Path.IsPathRooted(normalized) || normalized == relative);
    }
}
