using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SteamShortcutsImporter;

/// <summary>
/// Represents a Steam user account from loginusers.vdf.
/// </summary>
public class SteamUserAccount
{
    public string UserId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string PersonaName { get; set; } = string.Empty;
    /// <summary>
    /// Display name prioritizes: PersonaName (Steam display name) > AccountName (login name) > fallback.
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(PersonaName)
        ? PersonaName
        : !string.IsNullOrEmpty(AccountName)
            ? AccountName
            : string.Format(Constants.SteamUserFallbackFormat, UserId);
}

/// <summary>
/// Reads Steam user accounts from config/loginusers.vdf (text VDF format).
/// </summary>
internal static class SteamUsersReader
{
    private static readonly ILogger Logger = LogManager.GetLogger();

    /// <summary>
    /// Reads loginusers.vdf and returns a mapping of userId to AccountName.
    /// </summary>
    /// <param name="steamRootPath">The Steam installation root path.</param>
    /// <returns>Dictionary mapping Steam user IDs to account info.</returns>
    public static Dictionary<string, SteamUserAccount> ReadUsers(string? steamRootPath)
    {
        var result = new Dictionary<string, SteamUserAccount>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(steamRootPath))
        {
            return result;
        }

        var loginusersPath = Path.Combine(steamRootPath, "config", "loginusers.vdf");
        if (!File.Exists(loginusersPath))
        {
            Logger.Debug($"loginusers.vdf not found at {loginusersPath}");
            return result;
        }

        try
        {
            // Read with UTF-8 BOM detection
            var content = File.ReadAllText(loginusersPath, Encoding.UTF8);
            ParseLoginUsers(content, result);
            Logger.Info($"Read {result.Count} user(s) from loginusers.vdf");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to read loginusers.vdf at {loginusersPath}");
        }

        return result;
    }

    /// <summary>
    /// Gets the list of valid Steam users (intersection of loginusers.vdf and userdata directories).
    /// </summary>
    /// <param name="steamRootPath">The Steam installation root path.</param>
    /// <param name="validUserIds">List of valid user IDs from userdata directories.</param>
    /// <returns>List of SteamUserAccount objects for valid users.</returns>
    public static List<SteamUserAccount> GetValidUsers(string? steamRootPath, IEnumerable<string> validUserIds)
    {
        var users = ReadUsers(steamRootPath);
        var validIdSet = new HashSet<string>(validUserIds, StringComparer.Ordinal);
        var result = new List<SteamUserAccount>();

        foreach (var userId in validIdSet)
        {
            if (users.TryGetValue(userId, out var account))
            {
                result.Add(account);
            }
            else
            {
                // User has userdata folder but not in loginusers.vdf - add with fallback name
                result.Add(new SteamUserAccount
                {
                    UserId = userId,
                    AccountName = string.Empty
                });
            }
        }

        return result;
    }

    private static void ParseLoginUsers(string content, Dictionary<string, SteamUserAccount> result)
    {
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var currentUserId = string.Empty;
        var currentUser = (SteamUserAccount?)null;
        var inUsersBlock = false;
        var inUserBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            // Look for "users" block
            if (line.StartsWith("\"users\"", StringComparison.OrdinalIgnoreCase))
            {
                inUsersBlock = true;
                continue;
            }

            if (!inUsersBlock)
            {
                continue;
            }

            // Opening brace for users block
            if (line == "{")
            {
                if (!inUserBlock)
                {
                    // This is the users block opening brace
                    continue;
                }
            }

            // Closing brace
            if (line == "}")
            {
                if (inUserBlock)
                {
                    // End of current user block
                    if (currentUser != null && !string.IsNullOrEmpty(currentUserId))
                    {
                        result[currentUserId] = currentUser;
                    }
                    currentUserId = string.Empty;
                    currentUser = null;
                    inUserBlock = false;
                }
                else
                {
                    // End of users block
                    inUsersBlock = false;
                }
                continue;
            }

            // Parse key-value pair: "key" "value"
            var (key, value) = ParseKeyValue(line);
            if (key == null)
            {
                continue;
            }

            // If the key is a Steam ID (numeric), it's a new user block
            if (!inUserBlock && IsSteamId(key))
            {
                currentUserId = key;
                currentUser = new SteamUserAccount { UserId = key };
                inUserBlock = true;
                continue;
            }

            // Parse user properties
            if (inUserBlock && currentUser != null)
            {
                if (string.Equals(key, "AccountName", StringComparison.OrdinalIgnoreCase))
                {
                    currentUser.AccountName = value ?? string.Empty;
                }
                else if (string.Equals(key, "PersonaName", StringComparison.OrdinalIgnoreCase))
                {
                    currentUser.PersonaName = value ?? string.Empty;
                }
            }
        }
    }

    private static (string? key, string? value) ParseKeyValue(string line)
    {
        // Format: "key" "value" or "key" "value" // comment
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                if (inQuotes)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                inQuotes = !inQuotes;
            }
            else if (inQuotes)
            {
                current.Append(c);
            }
            else if (c == '/' && parts.Count >= 2)
            {
                // Start of comment, stop parsing
                break;
            }
        }

        if (parts.Count >= 2)
        {
            return (parts[0], parts[1]);
        }

        if (parts.Count == 1)
        {
            return (parts[0], null);
        }

        return (null, null);
    }

    private static bool IsSteamId(string value)
    {
        // Steam IDs are 17-digit numbers (SteamID64)
        if (string.IsNullOrEmpty(value) || value.Length != 17)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
