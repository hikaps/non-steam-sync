using System.Collections.Generic;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class ShortcutsFileTests
{
    [Fact]
    public void ToObject_UnquotedExe_WrapsInQuotes()
    {
        var sc = new SteamShortcut { Exe = @"C:\Game\game.exe", AppName = "Test" };

        var obj = ShortcutsFile.ToObject(sc);

        Assert.Equal("\"C:\\Game\\game.exe\"", obj["Exe"]);
    }

    [Fact]
    public void ToObject_AlreadyQuotedExe_PreservesQuotes()
    {
        var sc = new SteamShortcut { Exe = "\"C:\\Game\\game.exe\"", AppName = "Test" };

        var obj = ShortcutsFile.ToObject(sc);

        Assert.Equal("\"C:\\Game\\game.exe\"", obj["Exe"]);
    }

    [Fact]
    public void ToObject_QuotedExeWithTrailingArgs_ExtractsQuotedPortion()
    {
        // Regression for #35: exe field contains a quoted path with arguments (split before
        // reaching ToObject since the SplitExeAndArgs fix, but this guards against the case).
        var sc = new SteamShortcut
        {
            Exe = "\"D:\\Games\\Portal Prelude\\hl2.exe\" -game portalprelude -steam",
            AppName = "Portal Prelude"
        };

        var obj = ShortcutsFile.ToObject(sc);

        // Should NOT be double-quoted to: ""D:\Games\..." -args"
        Assert.Equal("\"D:\\Games\\Portal Prelude\\hl2.exe\"", obj["Exe"]);
    }

    [Fact]
    public void ToObject_QuotedExeOnlyUnmatchedQuote_WrapsEntireString()
    {
        // Leading quote with no closing quote — treat entire string as exe and wrap
        var sc = new SteamShortcut { Exe = "\"broken", AppName = "Test" };

        var obj = ShortcutsFile.ToObject(sc);

        Assert.Equal("\"\"broken\"", obj["Exe"]);
    }

    [Fact]
    public void ToObject_EmptyExe_StaysEmpty()
    {
        var sc = new SteamShortcut { Exe = "", AppName = "Test" };

        var obj = ShortcutsFile.ToObject(sc);

        Assert.Equal("", obj["Exe"]);
    }

    [Fact]
    public void ToObject_WhitespaceOnlyExe_StaysEmpty()
    {
        var sc = new SteamShortcut { Exe = "   ", AppName = "Test" };

        var obj = ShortcutsFile.ToObject(sc);

        Assert.Equal("", obj["Exe"]);
    }
}
