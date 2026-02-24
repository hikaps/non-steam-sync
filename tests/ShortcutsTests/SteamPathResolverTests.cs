using System;
using System.Collections.Generic;
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

    #region Executable Discovery Tests

    [Fact]
    public void ScanForExecutables_FindsExesInRootDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "other.exe"), "test");

            var resolver = new SteamPathResolver(null);
            var results = resolver.ScanForExecutables(tempDir);

            Assert.Equal(2, results.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ScanForExecutables_FiltersExcludedPatterns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "uninstall.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "UnityCrashHandler64.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "setup.exe"), "test");

            var resolver = new SteamPathResolver(null);
            var results = resolver.ScanForExecutables(tempDir);

            Assert.Single(results);
            Assert.Contains("game.exe", results[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ScanForExecutables_ScansSubdirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var binDir = Path.Combine(tempDir, "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "game.exe"), "test");

            var resolver = new SteamPathResolver(null);
            var results = resolver.ScanForExecutables(tempDir, maxDepth: 2);

            Assert.Single(results);
            Assert.Contains("game.exe", results[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ScanForExecutables_RespectsMaxDepth()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var deepDir = Path.Combine(tempDir, "level1", "level2", "level3");
            Directory.CreateDirectory(deepDir);
            File.WriteAllText(Path.Combine(deepDir, "game.exe"), "test");

            var resolver = new SteamPathResolver(null);
            var results = resolver.ScanForExecutables(tempDir, maxDepth: 2);

            // Should not find exe at depth 3
            Assert.Empty(results);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ScanForExecutables_SkipsExcludedDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var redistDir = Path.Combine(tempDir, "redist");
            Directory.CreateDirectory(redistDir);
            File.WriteAllText(Path.Combine(redistDir, "vcredist_x64.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "test");

            var resolver = new SteamPathResolver(null);
            var results = resolver.ScanForExecutables(tempDir);

            Assert.Single(results);
            Assert.Contains("game.exe", results[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SelectBestExecutable_PrefersNameMatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var candidates = new List<string>
            {
                Path.Combine(tempDir, "other.exe"),
                Path.Combine(tempDir, "MyGame.exe"),
                Path.Combine(tempDir, "launcher.exe")
            };
            foreach (var c in candidates)
                File.WriteAllText(c, "test");

            var resolver = new SteamPathResolver(null);
            var result = resolver.SelectBestExecutable(candidates, "My Game", tempDir);

            Assert.NotNull(result);
            Assert.Contains("MyGame.exe", result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SelectBestExecutable_ReturnsNullWhenAmbiguous()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var candidates = new List<string>
            {
                Path.Combine(tempDir, "foo.exe"),
                Path.Combine(tempDir, "bar.exe"),
                Path.Combine(tempDir, "baz.exe")
            };
            foreach (var c in candidates)
                File.WriteAllText(c, "test");

            var resolver = new SteamPathResolver(null);
            var result = resolver.SelectBestExecutable(candidates, "Completely Different Name", tempDir);

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseGogManifest_ValidManifest_ReturnsExePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gog_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Create a mock GOG manifest
            var manifestContent = @"{
                ""gameId"": ""1234567890"",
                ""name"": ""Test Game"",
                ""playTasks"": [{
                    ""isPrimary"": true,
                    ""path"": ""game.exe"",
                    ""type"": ""FileTask""
                }]
            }";
            File.WriteAllText(Path.Combine(tempDir, "goggame-1234567890.info"), manifestContent);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "test");

            var resolver = new SteamPathResolver(null);
            var result = resolver.TryParseGogManifest(tempDir, "1234567890");

            Assert.NotNull(result);
            Assert.Contains("game.exe", result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseGogManifest_MissingManifest_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gog_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);

            var resolver = new SteamPathResolver(null);
            var result = resolver.TryParseGogManifest(tempDir, "1234567890");

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseGogManifest_ExeNotFound_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gog_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Create manifest but no exe
            var manifestContent = @"{
                ""playTasks"": [{
                    ""path"": ""nonexistent.exe""
                }]
            }";
            File.WriteAllText(Path.Combine(tempDir, "goggame-123.info"), manifestContent);

            var resolver = new SteamPathResolver(null);
            var result = resolver.TryParseGogManifest(tempDir, "123");

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryDiscoverExecutable_NoInstallDir_ReturnsNull()
    {
        var game = new Game("Test Game")
        {
            InstallDirectory = null
        };

        var resolver = new SteamPathResolver(null);
        var result = resolver.TryDiscoverExecutable(game);

        Assert.Null(result);
    }

    [Fact]
    public void TryDiscoverExecutable_SingleExe_AutoSelects()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "test");

            var game = new Game("Test Game")
            {
                InstallDirectory = tempDir
            };

            var resolver = new SteamPathResolver(null);
            var result = resolver.TryDiscoverExecutable(game);

            Assert.NotNull(result);
            Assert.Contains("game.exe", result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryDiscoverExecutable_MultipleExesWithNameMatch_AutoSelects()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "TestGame.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "other.exe"), "test");

            var game = new Game("Test Game")
            {
                InstallDirectory = tempDir
            };

            var resolver = new SteamPathResolver(null);
            var result = resolver.TryDiscoverExecutable(game);

            Assert.NotNull(result);
            Assert.Contains("TestGame.exe", result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryDiscoverExecutable_AmbiguousExes_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "exe_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "foo.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "bar.exe"), "test");
            File.WriteAllText(Path.Combine(tempDir, "baz.exe"), "test");

            var game = new Game("Completely Different Name")
            {
                InstallDirectory = tempDir
            };

            var resolver = new SteamPathResolver(null);
            var result = resolver.TryDiscoverExecutable(game);

            // Should return null because no clear match
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
    #endregion

    #region ResolveShortcutsVdfPathForUser Tests

    [Fact]
    public void ResolveShortcutsVdfPathForUser_WithValidUserId_ReturnsCorrectPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var userDataDir = Path.Combine(tempRoot, "userdata", "12345678901234567", "config");
            Directory.CreateDirectory(userDataDir);
            var vdfPath = Path.Combine(userDataDir, "shortcuts.vdf");
            File.WriteAllText(vdfPath, "test");

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPathForUser("12345678901234567");

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
    public void ResolveShortcutsVdfPathForUser_WithNullUserId_FallsBackToAutoDetect()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var userDataDir = Path.Combine(tempRoot, "userdata", "99999999999999999", "config");
            Directory.CreateDirectory(userDataDir);
            var vdfPath = Path.Combine(userDataDir, "shortcuts.vdf");
            File.WriteAllText(vdfPath, "test");

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPathForUser(null);

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
    public void ResolveShortcutsVdfPathForUser_WithEmptyUserId_FallsBackToAutoDetect()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var userDataDir = Path.Combine(tempRoot, "userdata", "11111111111111111", "config");
            Directory.CreateDirectory(userDataDir);
            var vdfPath = Path.Combine(userDataDir, "shortcuts.vdf");
            File.WriteAllText(vdfPath, "test");

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPathForUser("");

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
    public void ResolveShortcutsVdfPathForUser_UserDirExistsButNoVdf_ReturnsPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var userDataDir = Path.Combine(tempRoot, "userdata", "12345678901234567", "config");
            Directory.CreateDirectory(userDataDir);
            // No shortcuts.vdf created

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPathForUser("12345678901234567");

            Assert.NotNull(result);
            Assert.Contains("12345678901234567", result);
            Assert.Contains("shortcuts.vdf", result);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveShortcutsVdfPathForUser_UserDirNotExist_FallsBackToAutoDetect()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Create a different user with vdf for fallback
            var fallbackDir = Path.Combine(tempRoot, "userdata", "99999999999999999", "config");
            Directory.CreateDirectory(fallbackDir);
            var fallbackVdf = Path.Combine(fallbackDir, "shortcuts.vdf");
            File.WriteAllText(fallbackVdf, "test");

            // Create userdata dir but not the specific user dir
            Directory.CreateDirectory(Path.Combine(tempRoot, "userdata"));

            var resolver = new SteamPathResolver(tempRoot);
            var result = resolver.ResolveShortcutsVdfPathForUser("12345678901234567");

            // Should fall back to auto-detect
            Assert.NotNull(result);
            Assert.Equal(fallbackVdf, result);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveShortcutsVdfPathForUser_InvalidSteamPath_ReturnsNull()
    {
        var invalidPath = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"));
        var resolver = new SteamPathResolver(invalidPath);

        var result = resolver.ResolveShortcutsVdfPathForUser("12345678901234567");

        Assert.Null(result);
    }

    #endregion
}
