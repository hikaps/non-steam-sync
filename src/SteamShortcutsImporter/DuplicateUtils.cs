using System;
using System.IO;

namespace SteamShortcutsImporter;

internal static class DuplicateUtils
{
    /// <summary>
    /// Converts a path to its absolute form for comparison purposes.
    /// Removes surrounding quotes and resolves relative paths.
    /// </summary>
    public static string GetAbsolutePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var unq = input.Trim('"');
        try { return Path.GetFullPath(unq); } catch { return unq; }
    }

    public static bool ArePathsEqual(string a, string b)
    {
        var an = GetAbsolutePath(a);
        var bn = GetAbsolutePath(b);
        return string.Equals(an, bn, StringComparison.OrdinalIgnoreCase);
    }

    public static string ExpectedRungameUrl(uint appId)
    {
        if (appId == 0) return string.Empty;
        var gid = Utils.ToShortcutGameId(appId);
        return $"steam://rungameid/{gid}";
    }
}

