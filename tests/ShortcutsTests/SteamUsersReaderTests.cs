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
    ""76561198000000001""
    {
        ""AccountName""       ""testuser1""
        ""PersonaName""       ""Test User 1""
    }
    ""76561198000000002""
    {
        ""AccountName""       ""testuser2""
        ""PersonaName""       ""Test User 2""
    }
}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var result = SteamUsersReader.ReadUsers(tempRoot);

            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("76561198000000001"));
            Assert.True(result.ContainsKey("76561198000000002"));
            Assert.Equal("testuser1", result["76561198000000001"].AccountName);
            Assert.Equal("testuser2", result["76561198000000002"].AccountName);
            Assert.Equal("Test User 1", result["76561198000000001"].PersonaName);
            Assert.Equal("Test User 2", result["76561198000000002"].PersonaName);
            Assert.Equal("Test User 1", result["76561198000000001"].DisplayName);
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
    ""76561198000000001""
    {
        ""AccountName""       ""testuser""
    }
    // incomplete entry
    ""76561198000000002""
    {
";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var result = SteamUsersReader.ReadUsers(tempRoot);

            // Should at least get the first user
            Assert.Single(result);
            Assert.True(result.ContainsKey("76561198000000001"));
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
    ""76561198000000001""
    {
        ""AccountName""       ""validuser""
        ""PersonaName""       ""Valid User""
    }
    ""76561198000000002""
    {
        ""AccountName""       ""invaliduser""
        ""PersonaName""       ""Invalid User""
    }
}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var validUserIds = new List<string> { "76561198000000001" };
            var result = SteamUsersReader.GetValidUsers(tempRoot, validUserIds);

            Assert.Single(result);
            Assert.Equal("validuser", result[0].AccountName);
            Assert.Equal("76561198000000001", result[0].UserId);
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
    ""76561198000000001""
    {
        ""AccountName""       ""testuser""
    }
}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            // This user ID is not in loginusers.vdf but is valid IDs
            var validUserIds = new List<string> { "76561198000000002" };
            var result = SteamUsersReader.GetValidUsers(tempRoot, validUserIds);

            Assert.Single(result);
            Assert.Equal("", result[0].AccountName);
            Assert.Equal("Steam User 76561198000000002", result[0].DisplayName);
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
    ""76561198000000001""
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

    [Fact]
    public void ReadUsers_RealWorldFormat_WithTabsAndExtraFields()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "steam_users_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var configDir = Path.Combine(tempRoot, "config");
            Directory.CreateDirectory(configDir);

            // Exact format from user's real loginusers.vdf with tabs
            var loginusersContent = "\"users\"\n{\n\t\"76561198051525001\"\n\t{\n\t\t\"AccountName\"\t\t\"testuser1\"\n\t\t\"PersonaName\"\t\t\"Test Display 1\"\n\t\t\"RememberPassword\"\t\t\"1\"\n\t\t\"WantsOfflineMode\"\t\t\"0\"\n\t\t\"SkipOfflineModeWarning\"\t\t\"0\"\n\t\t\"AllowAutoLogin\"\t\t\"1\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t\t\"Timestamp\"\t\t\"1772589956\"\n\t}\n\t\"76561198391833687\"\n\t{\n\t\t\"AccountName\"\t\t\"testuser2\"\n\t\t\"PersonaName\"\t\t\"Test Display 2\"\n\t\t\"RememberPassword\"\t\t\"1\"\n\t\t\"WantsOfflineMode\"\t\t\"0\"\n\t\t\"SkipOfflineModeWarning\"\t\t\"0\"\n\t\t\"AllowAutoLogin\"\t\t\"0\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t\t\"Timestamp\"\t\t\"1771962844\"\n\t}\n}";
            File.WriteAllText(Path.Combine(configDir, "loginusers.vdf"), loginusersContent);

            var result = SteamUsersReader.ReadUsers(tempRoot);

            Assert.Equal(2, result.Count);
            Assert.Equal("testuser1", result["76561198051525001"].AccountName);
            Assert.Equal("Test Display 1", result["76561198051525001"].PersonaName);
            Assert.Equal("Test Display 1", result["76561198051525001"].DisplayName);
            Assert.Equal("Test Display 2", result["76561198391833687"].DisplayName);
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
            UserId = "76561198000000001",
            AccountName = "testuser"
        };

        Assert.Equal("testuser", account.DisplayName);
    }

    [Fact]
    public void DisplayName_WithEmptyAccountName_ReturnsFallbackFormat()
    {
        var account = new SteamUserAccount
        {
            UserId = "76561198000000001",
            AccountName = ""
        };

        Assert.Equal("Steam User 76561198000000001", account.DisplayName);
    }

    [Fact]
    public void DisplayName_WithPersonaName_ReturnsPersonaName()
    {
        var account = new SteamUserAccount
        {
            UserId = "76561198000000001",
            AccountName = "loginname",
            PersonaName = "Display Name"
        };

        Assert.Equal("Display Name", account.DisplayName);
    }

    [Fact]
    public void DisplayName_PersonaNameOverridesAccountName()
    {
        var account = new SteamUserAccount
        {
            UserId = "76561198000000001",
            AccountName = "loginname",
            PersonaName = "Friendly Name"
        };

        // PersonaName should take priority over AccountName
        Assert.Equal("Friendly Name", account.DisplayName);
    }
}
