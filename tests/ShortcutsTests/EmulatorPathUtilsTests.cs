using Xunit;

namespace SteamShortcutsImporter.Tests;

public class EmulatorPathUtilsTests
{
    #region QuoteArgumentsIfNeeded Tests

    [Fact]
    public void QuoteArgumentsIfNeeded_WithNull_ReturnsEmpty()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_WithEmpty_ReturnsEmpty()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_WithWhitespaceOnly_ReturnsEmpty()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("   ");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_NoSpaces_ReturnsAsIs()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("--fullscreen");
        Assert.Equal("--fullscreen", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_SimplePathWithSpaces_GetsQuoted()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("C:\\Games\\My Game\\rom.gb");
        Assert.Equal("\"C:\\Games\\My Game\\rom.gb\"", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_AlreadyFullyQuoted_ReturnsAsIs()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("\"C:\\Games\\My Game\\rom.gb\"");
        Assert.Equal("\"C:\\Games\\My Game\\rom.gb\"", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_ContainsQuotedSegment_ReturnsAsIs()
    {
        // Arguments with already-quoted parts should not be re-quoted
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("-f \"C:\\path with space\\file.rom\"");
        Assert.Equal("-f \"C:\\path with space\\file.rom\"", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_StartsWithFlag_ReturnsAsIs()
    {
        // Arguments starting with flags should not be quoted
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("--flag with spaces");
        Assert.Equal("--flag with spaces", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_StartsWithSlash_ReturnsAsIs()
    {
        // Arguments starting with / (Windows style) should not be quoted
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("/C echo hello world");
        Assert.Equal("/C echo hello world", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_MultipleQuotes_ReturnsAsIs()
    {
        // Multiple quotes in the string means it's complex, don't re-quote
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("\"path1\" \"path2 with spaces\"");
        Assert.Equal("\"path1\" \"path2 with spaces\"", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_TrimsWhitespace()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("  C:\\path\\file.gb  ");
        Assert.Equal("C:\\path\\file.gb", result);
    }

    [Fact]
    public void QuoteArgumentsIfNeeded_TrimsThenQuotes()
    {
        var result = EmulatorPathUtils.QuoteArgumentsIfNeeded("  C:\\path with space\\file.gb  ");
        Assert.Equal("\"C:\\path with space\\file.gb\"", result);
    }

    #endregion

    #region IsRegexPattern Tests

    [Fact]
    public void IsRegexPattern_WithNull_ReturnsFalse()
    {
        var result = EmulatorPathUtils.IsRegexPattern(null);
        Assert.False(result);
    }

    [Fact]
    public void IsRegexPattern_WithEmpty_ReturnsFalse()
    {
        var result = EmulatorPathUtils.IsRegexPattern("");
        Assert.False(result);
    }

    [Fact]
    public void IsRegexPattern_StartsWithAnchor_ReturnsTrue()
    {
        var result = EmulatorPathUtils.IsRegexPattern("^retroarch");
        Assert.True(result);
    }

    [Fact]
    public void IsRegexPattern_EndsWithAnchor_ReturnsTrue()
    {
        var result = EmulatorPathUtils.IsRegexPattern("retroarch$");
        Assert.True(result);
    }

    [Fact]
    public void IsRegexPattern_ContainsDigitClass_ReturnsTrue()
    {
        var result = EmulatorPathUtils.IsRegexPattern("emu\\d+.exe");
        Assert.True(result);
    }

    [Fact]
    public void IsRegexPattern_ContainsWordClass_ReturnsTrue()
    {
        var result = EmulatorPathUtils.IsRegexPattern("\\w+.exe");
        Assert.True(result);
    }

    [Fact]
    public void IsRegexPattern_PlainFilename_ReturnsFalse()
    {
        // setup_v1.0.exe should NOT be detected as regex (no longer matches \\.)
        var result = EmulatorPathUtils.IsRegexPattern("setup_v1.0.exe");
        Assert.False(result);
    }

    [Fact]
    public void IsRegexPattern_PlainExeName_ReturnsFalse()
    {
        var result = EmulatorPathUtils.IsRegexPattern("retroarch.exe");
        Assert.False(result);
    }

    [Fact]
    public void IsRegexPattern_StarWildcard_ReturnsFalse()
    {
        // *.exe is a glob pattern, not regex (no longer matches .*)
        var result = EmulatorPathUtils.IsRegexPattern("*.exe");
        Assert.False(result);
    }

    [Fact]
    public void IsRegexPattern_WildcardDotStar_ReturnsTrue()
    {
        var result = EmulatorPathUtils.IsRegexPattern(".*.exe");
        Assert.True(result);
    }

    #endregion
}
