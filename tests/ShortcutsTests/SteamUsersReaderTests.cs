using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class SteamUsersReaderTests
{
    [Fact]
    public void ReadUsers_ValidLoginUsersVdf_ReturnsUsers()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_users_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var configDir = Path.Combine(tempRoot, "config");
            Directory.CreateDirectory(configDir);

            var loginusersContent = @"""users""
{
    ""12345678901234567""
    {
        ""AccountName""       ""testuser1""
        ""PersonaName""       ""Test User 1""
    }
    ""98765432109876543""
    {
        ""AccountName""       ""testuser2""
        ""PersonaName""       ""Test User 2""
    }
}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var result = SteamUsersReader.ReadUsers(tempRoot);

            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("12345678901234567"));
            Assert.True(result.ContainsKey("98765432109876543"));
            Assert.Equal("testuser1", result["12345678901234567"].AccountName);
            Assert.Equal("testuser2", result["98765432109876543"].AccountName);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ReadUsers_NoLoginUsersFile_ReturnsEmpty()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_users_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            // No config/loginusers.vdf created

            var result = SteamUsersReader.ReadUsers(tempRoot);

            Assert.Empty(result);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ReadUsers_NullPath_ReturnsEmpty()
    {
        var result = SteamUsersReader.ReadUsers(null);
        Assert.Empty(result);
    }

    [Fact]
    public void ReadUsers_EmptyPath_ReturnsEmpty()
    {
        var result = SteamUsersReader.ReadUsers("");
        Assert.Empty(result);
    }

    [Fact]
    public void ReadUsers_MalformedVdf_ReturnsWhatItCan()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_users_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var configDir = Path.Combine(tempRoot, "config");
            Directory.CreateDirectory(configDir);

            var loginusersContent = @"""users""
{
    ""12345678901234567""
    {
        ""AccountName""       ""testuser""
    }
    // incomplete entry
    ""99999999999999999""
    {
";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var result = SteamUsersReader.ReadUsers(tempRoot);

            // Should at least get the first user
            Assert.Single(result);
            Assert.True(result.ContainsKey("12345678901234567"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void GetValidUsers_WithMatchingUserIds_ReturnsMatchingUsers()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_users_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var configDir = Path.Combine(tempRoot, "config");
            Directory.CreateDirectory(configDir);

            var loginusersContent = @"""users""
{
    ""12345678901234567""
    {
        ""AccountName""       ""validuser""
        ""PersonaName""       ""Valid User""
    }
    ""98765432109876543""
    {
        ""AccountName""       ""invaliduser""
        ""PersonaName""       ""Invalid User""
    }
}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var validUserIds = new List<string> { "12345678901234567" };
            var result = SteamUsersReader.GetValidUsers(tempRoot, validUserIds);

            Assert.Single(result);
            Assert.Equal("validuser", result[0].AccountName);
            Assert.Equal("12345678901234567", result[0].UserId);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void GetValidUsers_UserIdNotInLoginUsers_ReturnsFallbackName()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_users_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var configDir = Path.Combine(tempRoot, "config");
            Directory.CreateDirectory(configDir);

            var loginusersContent = @"""users""
{
    ""12345678901234567""
    {
        ""AccountName""       ""testuser""
    }
}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            // This user ID is not in loginusers.vdf but is valid IDs
            var validUserIds = new List<string> { "99999999999999999" };
            var result = SteamUsersReader.GetValidUsers(tempRoot, validUserIds);

            Assert.Single(result);
            Assert.Equal("", result[0].AccountName);
            Assert.Equal("Steam User 99999999999999999", result[0].DisplayName);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void GetValidUsers_EmptyValidUserIds_ReturnsEmpty()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_users_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var configDir = Path.Combine(tempRoot, "config");
            Directory.CreateDirectory(configDir);

            var loginusersContent = @"""users""
{
    ""12345678901234567""
    {
        ""AccountName""       ""testuser""
    }
}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var result = SteamUsersReader.GetValidUsers(tempRoot, new List<string>());

            Assert.Empty(result);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}

public class SteamUserAccountTests
{
    [Fact]
    public void DisplayName_WithAccountName_ReturnsAccountName()
    {
        var account = new SteamUserAccount
        {
            UserId = "12345678901234567",
            AccountName = "testuser"
        };

        Assert.Equal("testuser", account.DisplayName);
    }

    [Fact]
    public void DisplayName_WithEmptyAccountName_ReturnsFallbackFormat()
    {
        var account = new SteamUserAccount
        {
            UserId = "12345678901234567",
            AccountName = ""
        };

        Assert.Equal("Steam User 12345678901234567", account.DisplayName);
    }
}
