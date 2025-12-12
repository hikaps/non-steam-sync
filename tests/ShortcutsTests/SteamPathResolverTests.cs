using System;
using System.IO;
using Playnite.SDK.Models;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class SteamPathResolverTests
{
    [Fact]
    public void ResolveShortcutsVdfPath_ValidSteamPath_ReturnsVdf()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Create Steam directory structure
            var userDataDir = Path.Combine(tempRoot, "userdata", "12345", "config");
            Directory.CreateDirectory(userDataDir);
            var vdfPath = Path.Combine(userDataDir, "shortcuts.vdf");
            File.WriteAllText(vdfPath, "test");

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPath();

            Assert.NotNull(result);
            Assert.Equal(vdfPath, result);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveShortcutsVdfPath_MultipleUsers_ReturnsFirstValid()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Create multiple user directories
            var user1Dir = Path.Combine(tempRoot, "userdata", "11111", "config");
            var user2Dir = Path.Combine(tempRoot, "userdata", "22222", "config");
            Directory.CreateDirectory(user1Dir);
            Directory.CreateDirectory(user2Dir);

            // Only second user has shortcuts.vdf
            var vdfPath = Path.Combine(user2Dir, "shortcuts.vdf");
            File.WriteAllText(vdfPath, "test");

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPath();

            Assert.NotNull(result);
            Assert.True(File.Exists(result));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveShortcutsVdfPath_InvalidSteamPath_ReturnsNull()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"));
        var resolver = new SteamPathResolver(invalidPath);

        var result = resolver.ResolveShortcutsVdfPath();

        Assert.Null(result);
    }

    [Fact]
    public void ResolveShortcutsVdfPath_NoUserData_ReturnsNull()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            // No userdata directory

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPath();

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveShortcutsVdfPath_NoConfigDir_ReturnsNull()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var userDataDir = Path.Combine(tempRoot, "userdata", "12345");
            Directory.CreateDirectory(userDataDir);
            // No config directory

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPath();

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveShortcutsVdfPath_NullSteamPath_ReturnsNull()
    {
        var resolver = new SteamPathResolver(null);
        var result = resolver.ResolveShortcutsVdfPath();

        Assert.Null(result);
    }

    [Fact]
    public void ExpandPathVariables_ReplacesInstallDir()
    {
        var game = new Game("Test Game")
        {
            InstallDirectory = "C:\\Games\\TestGame"
        };

        var resolver = new SteamPathResolver(null);
        var result = resolver.ExpandPathVariables(game, "{InstallDir}\\game.exe");

        Assert.Contains("TestGame", result);
        Assert.DoesNotContain("{InstallDir}", result);
    }

    [Fact]
    public void ExpandPathVariables_ExpandsEnvironmentVariables()
    {
        var game = new Game("Test Game");
        var resolver = new SteamPathResolver(null);
        
        // Use TEMP on Windows, HOME on Unix
        string envVar = OperatingSystem.IsWindows() ? "TEMP" : "HOME";
        string input = OperatingSystem.IsWindows() ? "%TEMP%\\test.exe" : "${HOME}/test.exe";
        
        var result = resolver.ExpandPathVariables(game, input);

        // Windows ExpandEnvironmentVariables expands %VAR%
        // Unix shells use $VAR or ${VAR}, but .NET ExpandEnvironmentVariables doesn't expand them
        // So on Unix, we just verify the method doesn't crash
        Assert.NotNull(result);
        
        if (OperatingSystem.IsWindows())
        {
            // On Windows, %TEMP% should be expanded
            Assert.DoesNotContain("%TEMP%", result ?? string.Empty);
        }
    }

    [Fact]
    public void ExpandPathVariables_HandlesRelativePaths()
    {
        var game = new Game("Test Game")
        {
            InstallDirectory = "C:\\Games\\TestGame"
        };

        var resolver = new SteamPathResolver(null);
        var result = resolver.ExpandPathVariables(game, "bin\\game.exe");

        // Should convert relative to absolute using InstallDirectory
        Assert.True(Path.IsPathRooted(result));
        Assert.Contains("TestGame", result);
    }

    [Fact]
    public void ExpandPathVariables_NullInput_ReturnsNull()
    {
        var game = new Game("Test Game");
        var resolver = new SteamPathResolver(null);

        var result = resolver.ExpandPathVariables(game, null);

        Assert.Null(result);
    }

    [Fact]
    public void ExpandPathVariables_EmptyInput_ReturnsEmpty()
    {
        var game = new Game("Test Game");
        var resolver = new SteamPathResolver(null);

        var result = resolver.ExpandPathVariables(game, "");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExpandPathVariables_WhitespaceInput_ReturnsWhitespace()
    {
        var game = new Game("Test Game");
        var resolver = new SteamPathResolver(null);

        var result = resolver.ExpandPathVariables(game, "   ");

        Assert.Equal("   ", result);
    }

    [Fact]
    public void ExpandPathVariables_QuotedPaths_UnquotesResult()
    {
        var game = new Game("Test Game")
        {
            InstallDirectory = "C:\\Games\\TestGame"
        };

        var resolver = new SteamPathResolver(null);
        var result = resolver.ExpandPathVariables(game, "\"{InstallDir}\\game.exe\"");

        Assert.DoesNotContain("\"", result ?? string.Empty);
        Assert.Contains("TestGame", result);
    }

    [Fact]
    public void TryParseAppIdFromRungameUrl_ValidUrl_ReturnsAppId()
    {
        var gameId = Utils.ToShortcutGameId(0x80000001u);
        var url = $"steam://rungameid/{gameId}";

        var appId = SteamPathResolver.TryParseAppIdFromRungameUrl(url);

        Assert.Equal(0x80000001u, appId);
    }

    [Fact]
    public void TryParseAppIdFromRungameUrl_InvalidUrl_ReturnsZero()
    {
        var result = SteamPathResolver.TryParseAppIdFromRungameUrl("https://example.com");

        Assert.Equal(0u, result);
    }

    [Fact]
    public void TryParseAppIdFromRungameUrl_NullUrl_ReturnsZero()
    {
        var result = SteamPathResolver.TryParseAppIdFromRungameUrl(null);

        Assert.Equal(0u, result);
    }

    [Fact]
    public void TryParseAppIdFromRungameUrl_MalformedUrl_ReturnsZero()
    {
        var result = SteamPathResolver.TryParseAppIdFromRungameUrl("steam://rungameid/notanumber");

        Assert.Equal(0u, result);
    }

    [Fact]
    public void TryParseAppIdFromRungameUrl_CaseInsensitive_Works()
    {
        var gameId = Utils.ToShortcutGameId(0x80000002u);
        var url = $"STEAM://RUNGAMEID/{gameId}";

        var appId = SteamPathResolver.TryParseAppIdFromRungameUrl(url);

        Assert.Equal(0x80000002u, appId);
    }

    [Fact]
    public void TryParseAppIdFromRungameUrl_ExtractsUpperBits()
    {
        // Create a game ID with known appId in upper 32 bits
        var expectedAppId = 0x90000123u;
        var gameId = Utils.ToShortcutGameId(expectedAppId);
        var url = $"steam://rungameid/{gameId}";

        var appId = SteamPathResolver.TryParseAppIdFromRungameUrl(url);

        Assert.Equal(expectedAppId, appId);
        // Verify high bit is set (Steam shortcut marker)
        Assert.True((appId & 0x80000000u) != 0);
    }
}
