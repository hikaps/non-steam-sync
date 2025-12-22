using System;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class UtilsTests
{
    [Theory]
    [InlineData("\"C:\\Games\\Foo.exe\"", "C:\\Games\\Foo.exe")]
    [InlineData("C:\\Games\\Foo.exe", "C:\\Games\\Foo.exe")]
    [InlineData("  C:\\Games\\Foo.exe  ", "C:\\Games\\Foo.exe")]
    [InlineData("\"  C:\\Games\\Foo.exe  \"", "  C:\\Games\\Foo.exe  ")]  // Quotes removed first, then inner spaces preserved
    [InlineData("", "")]
    [InlineData(null!, "")]
    [InlineData("   ", "")]
    [InlineData("\"\"", "")]
    [InlineData("\"C:\\Path\"", "C:\\Path")]
    [InlineData("/home/user/game", "/home/user/game")]
    [InlineData("\"/home/user/game\"", "/home/user/game")]
    public void NormalizePath_HandlesQuotesAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, Utils.NormalizePath(input));
    }

    [Fact]
    public void GenerateShortcutAppId_ConsistentAcrossQuotedAndUnquoted()
    {
        var exe1 = "C:\\Games\\Test\\game.exe";
        var exe2 = "\"C:\\Games\\Test\\game.exe\"";
        var name = "Test Game";

        var appId1 = Utils.GenerateShortcutAppId(exe1, name);
        var appId2 = Utils.GenerateShortcutAppId(exe2, name);

        Assert.Equal(appId1, appId2);
    }

    [Fact]
    public void GenerateShortcutAppId_HasHighBitSet()
    {
        var appId = Utils.GenerateShortcutAppId("game.exe", "Game");
        
        // Steam shortcuts always have 0x80000000 flag set
        Assert.True((appId & 0x80000000u) != 0);
    }

    [Fact]
    public void GenerateShortcutAppId_DifferentForDifferentGames()
    {
        var appId1 = Utils.GenerateShortcutAppId("game1.exe", "Game 1");
        var appId2 = Utils.GenerateShortcutAppId("game2.exe", "Game 2");
        
        Assert.NotEqual(appId1, appId2);
    }

    [Fact]
    public void GenerateShortcutAppId_HandlesNullAndEmpty()
    {
        // Should not throw
        var appId1 = Utils.GenerateShortcutAppId(null!, "Game");
        var appId2 = Utils.GenerateShortcutAppId("game.exe", null!);
        var appId3 = Utils.GenerateShortcutAppId("", "");
        
        Assert.True((appId1 & 0x80000000u) != 0);
        Assert.True((appId2 & 0x80000000u) != 0);
        Assert.True((appId3 & 0x80000000u) != 0);
    }

    [Fact]
    public void GenerateShortcutAppId_TrimsNameWhitespace()
    {
        var appId1 = Utils.GenerateShortcutAppId("game.exe", "Test Game");
        var appId2 = Utils.GenerateShortcutAppId("game.exe", "  Test Game  ");
        
        Assert.Equal(appId1, appId2);
    }

    [Fact]
    public void ToShortcutGameId_GeneratesCorrectFormat()
    {
        uint appId = 0x80000001u;
        ulong gameId = Utils.ToShortcutGameId(appId);
        
        // Expected: (appId << 32) | 0x02000000
        ulong expected = ((ulong)appId << 32) | 0x02000000UL;
        Assert.Equal(expected, gameId);
    }

    [Fact]
    public void HashString_ConsistentForSameInput()
    {
        var input = "test string";
        var hash1 = Utils.HashString(input);
        var hash2 = Utils.HashString(input);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashString_DifferentForDifferentInput()
    {
        var hash1 = Utils.HashString("test1");
        var hash2 = Utils.HashString("test2");
        
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Crc32_MatchesExpectedValue()
    {
        // Known test vector
        var data = System.Text.Encoding.UTF8.GetBytes("123456789");
        var crc = Utils.Crc32(data);
        
        // Standard CRC32 of "123456789" is 0xCBF43926
        Assert.Equal(0xCBF43926u, crc);
    }

    [Fact]
    public void HashString_EmptyString_ReturnsValidHash()
    {
        var hash = Utils.HashString("");
        
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Equal(16, hash.Length); // 64-bit hex = 16 chars
    }

    [Fact]
    public void HashString_WhitespaceOnly_ReturnsValidHash()
    {
        var hash1 = Utils.HashString(" ");
        var hash2 = Utils.HashString("  ");
        var hash3 = Utils.HashString("\t");
        
        Assert.False(string.IsNullOrEmpty(hash1));
        Assert.False(string.IsNullOrEmpty(hash2));
        Assert.False(string.IsNullOrEmpty(hash3));
        
        // Different whitespace should produce different hashes
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashString_UnicodeCharacters_HandlesCorrectly()
    {
        var hash1 = Utils.HashString("日本語");
        var hash2 = Utils.HashString("Émojis 🎮");
        
        Assert.False(string.IsNullOrEmpty(hash1));
        Assert.False(string.IsNullOrEmpty(hash2));
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashString_LongString_ReturnsValidHash()
    {
        var longString = new string('a', 10000);
        var hash = Utils.HashString(longString);
        
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Equal(16, hash.Length);
    }

    [Fact]
    public void Crc32_EmptyArray_ReturnsZero()
    {
        var crc = Utils.Crc32(Array.Empty<byte>());
        
        // CRC32 of empty input is 0x00000000
        Assert.Equal(0u, crc);
    }

    [Fact]
    public void Crc32_SingleByte_ReturnsExpectedValue()
    {
        // Known test case: CRC32 of single byte 0x00 is 0xD202EF8D
        var crc = Utils.Crc32(new byte[] { 0x00 });
        Assert.Equal(0xD202EF8Du, crc);
    }

    [Fact]
    public void GenerateShortcutAppId_SameExeDifferentName_DifferentIds()
    {
        var appId1 = Utils.GenerateShortcutAppId("game.exe", "Game 1");
        var appId2 = Utils.GenerateShortcutAppId("game.exe", "Game 2");
        
        Assert.NotEqual(appId1, appId2);
    }

    [Fact]
    public void GenerateShortcutAppId_SameNameDifferentExe_DifferentIds()
    {
        var appId1 = Utils.GenerateShortcutAppId("game1.exe", "Game");
        var appId2 = Utils.GenerateShortcutAppId("game2.exe", "Game");
        
        Assert.NotEqual(appId1, appId2);
    }

    [Fact]
    public void ToShortcutGameId_ZeroAppId_ReturnsBaseValue()
    {
        ulong gameId = Utils.ToShortcutGameId(0);
        
        // Should still return the base 0x02000000
        Assert.Equal(0x02000000UL, gameId);
    }

    [Fact]
    public void ToShortcutGameId_MaxAppId_HandlesCorrectly()
    {
        ulong gameId = Utils.ToShortcutGameId(uint.MaxValue);
        
        ulong expected = ((ulong)uint.MaxValue << 32) | 0x02000000UL;
        Assert.Equal(expected, gameId);
    }

    [Fact]
    public void NormalizePath_OnlyQuotes_ReturnsEmpty()
    {
        Assert.Equal("", Utils.NormalizePath("\"\""));
    }

    [Fact]
    public void NormalizePath_SingleQuote_ReturnsAsIs()
    {
        // Single quote at start or end is not a pair, so preserved after trim
        Assert.Equal("\"", Utils.NormalizePath("\""));
        Assert.Equal("test\"", Utils.NormalizePath("test\""));
        Assert.Equal("\"test", Utils.NormalizePath("\"test"));
    }

    [Fact]
    public void NormalizePath_NestedQuotes_RemovesOuterOnly()
    {
        // "\"path\"" -> "path" (outer quotes removed)
        Assert.Equal("\"path\"", Utils.NormalizePath("\"\"path\"\""));
    }
}
