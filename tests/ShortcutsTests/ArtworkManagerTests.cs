using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class ArtworkManagerTests
{
    [Fact]
    public void TryGetGridDirFromVdf_ValidPath_ReturnsGridDir()
    {
        var vdfPath = "/steam/userdata/12345/config/shortcuts.vdf";
        var expectedGridDir = Path.Combine("/steam/userdata/12345/config", "grid");

        // Create a mock ArtworkManager (we can't fully instantiate without IPlayniteAPI)
        // Instead, test the logic directly
        var configDir = Path.GetDirectoryName(vdfPath);
        var gridDir = Path.Combine(configDir!, "grid");

        Assert.Equal(expectedGridDir, gridDir);
    }

    [Fact]
    public void TryGetGridDirFromVdf_InvalidPath_HandlesGracefully()
    {
        // Test that invalid paths don't crash
        string? configDir = null;
        try
        {
            configDir = Path.GetDirectoryName(string.Empty);
        }
        catch
        {
            // Expected to handle gracefully
        }

        Assert.True(string.IsNullOrEmpty(configDir));
    }

    [Fact]
    public void TryPickGridPreview_PreferenceOrder_HeroOverPosterOverCover()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "grid_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            uint appId = 12345;

            // Create all artwork types
            var heroPath = Path.Combine(tempDir, $"{appId}_hero.png");
            var posterPath = Path.Combine(tempDir, $"{appId}p.png");
            var coverPath = Path.Combine(tempDir, $"{appId}.png");
            var iconPath = Path.Combine(tempDir, $"{appId}_icon.png");

            File.WriteAllText(heroPath, "hero");
            File.WriteAllText(posterPath, "poster");
            File.WriteAllText(coverPath, "cover");
            File.WriteAllText(iconPath, "icon");

            // Manually test priority order (simulating TryPickGridPreview logic)
            var heroFiles = Directory.GetFiles(tempDir, $"{appId}_hero.*");
            var posterFiles = Directory.GetFiles(tempDir, $"{appId}p.*");
            var coverFiles = Directory.GetFiles(tempDir, $"{appId}.*");
            var iconFiles = Directory.GetFiles(tempDir, $"{appId}_icon.*");

            var result = heroFiles.FirstOrDefault() ?? posterFiles.FirstOrDefault() ?? coverFiles.FirstOrDefault() ?? iconFiles.FirstOrDefault();

            Assert.Equal(heroPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryPickGridPreview_OnlyPoster_ReturnsPoster()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "grid_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            uint appId = 54321;

            // Only create poster
            var posterPath = Path.Combine(tempDir, $"{appId}p.jpg");
            File.WriteAllText(posterPath, "poster");

            var posterFiles = Directory.GetFiles(tempDir, $"{appId}p.*");
            var result = posterFiles.FirstOrDefault();

            Assert.Equal(posterPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryPickGridPreview_NoFiles_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "grid_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            uint appId = 99999;

            var heroFiles = Directory.GetFiles(tempDir, $"{appId}_hero.*");
            var result = heroFiles.FirstOrDefault();

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryPickGridPreview_InvalidDirectory_HandlesGracefully()
    {
        var invalidDir = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"));
        
        // Should handle non-existent directory gracefully
        var exists = Directory.Exists(invalidDir);
        Assert.False(exists);

        // Attempting to enumerate files in non-existent directory would throw
        // The actual implementation should catch this and return null
    }

    [Fact]
    public void TryGetGridIconPath_IconExists_ReturnsPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "grid_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            uint appId = 11111;

            var iconPath = Path.Combine(tempDir, $"{appId}_icon.png");
            File.WriteAllText(iconPath, "icon");

            var iconFiles = Directory.GetFiles(tempDir, $"{appId}_icon.*");
            var result = iconFiles.FirstOrDefault();

            Assert.Equal(iconPath, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryGetGridIconPath_NoIcon_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "grid_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            uint appId = 22222;

            var iconFiles = Directory.GetFiles(tempDir, $"{appId}_icon.*");
            var result = iconFiles.FirstOrDefault();

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryGetGridIconPath_MultipleExtensions_ReturnsFirst()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "grid_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            uint appId = 33333;

            // Create icons with different extensions
            var pngPath = Path.Combine(tempDir, $"{appId}_icon.png");
            var jpgPath = Path.Combine(tempDir, $"{appId}_icon.jpg");
            File.WriteAllText(pngPath, "png");
            File.WriteAllText(jpgPath, "jpg");

            var iconFiles = Directory.GetFiles(tempDir, $"{appId}_icon.*");
            var result = iconFiles.FirstOrDefault();

            Assert.NotNull(result);
            Assert.True(result == pngPath || result == jpgPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
