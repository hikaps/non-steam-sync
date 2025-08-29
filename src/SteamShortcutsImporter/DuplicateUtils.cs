using System;
using System.IO;

namespace SteamShortcutsImporter;

internal static class DuplicateUtils
{
    public static string NormalizePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var unq = input.Trim('"');
        try { return Path.GetFullPath(unq); } catch { return unq; }
    }

    public static bool ArePathsEqual(string a, string b)
    {
        var an = NormalizePath(a);
        var bn = NormalizePath(b);
        return string.Equals(an, bn, StringComparison.OrdinalIgnoreCase);
    }

    public static string ExpectedRungameUrl(uint appId)
    {
        if (appId == 0) return string.Empty;
        var gid = Utils.ToShortcutGameId(appId);
        return $"steam://rungameid/{gid}";
    }
}

