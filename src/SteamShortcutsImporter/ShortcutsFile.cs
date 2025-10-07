using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamShortcutsImporter;

public class SteamShortcut
{
    public string AppName { get; set; } = string.Empty;
    public string Exe { get; set; } = string.Empty;
    public string StartDir { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ShortcutPath { get; set; } = string.Empty;
    public string LaunchOptions { get; set; } = string.Empty;
    public uint AppId { get; set; }
    public int IsHidden { get; set; } = 0;
    public int AllowDesktopConfig { get; set; } = 1;
    public int AllowOverlay { get; set; } = 1;
    public int OpenVR { get; set; } = 0;
    public List<string>? Tags { get; set; }

    // A stable id derived from exe + app name similar to Steam's appid seed.
    public string StableId => Utils.HashString($"{Exe}|{AppName}");
}

public static class ShortcutsFile
{
    public static IEnumerable<SteamShortcut> Read(string path)
    {
        using var fs = File.OpenRead(path);
        var root = BinaryKv.ReadObject(fs);
        var list = new List<SteamShortcut>();
        if (root.TryGetValue(Constants.ShortcutsKey, out var shortcutsNode) && shortcutsNode is Dictionary<string, object> shortcuts)
        {
            foreach (var kv in shortcuts.OrderBy(k => k.Key))
            {
                if (kv.Value is Dictionary<string, object> sc)
                {
                    list.Add(ParseShortcut(sc));
                }
            }
        }
        return list;
    }

    public static void Write(string path, IEnumerable<SteamShortcut> shortcuts)
    {
        // Build object tree
        var root = new Dictionary<string, object>();
        var shortcutsObj = new Dictionary<string, object>();
        int idx = 0;
        foreach (var sc in shortcuts)
        {
            shortcutsObj[idx.ToString()] = ToObject(sc);
            idx++;
        }
        root[Constants.ShortcutsKey] = shortcutsObj;

        // Ensure directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        BinaryKv.WriteObject(fs, root);
        fs.Flush(true);
    }

    private static SteamShortcut ParseShortcut(Dictionary<string, object> obj)
    {
        string GetStr(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (obj.TryGetValue(k, out var v))
                {
                    return v?.ToString() ?? string.Empty;
                }
            }
            // case-insensitive fallback
            foreach (var kv in obj)
            {
                if (keys.Any(k => string.Equals(k, kv.Key, System.StringComparison.OrdinalIgnoreCase)))
                {
                    return kv.Value?.ToString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        int GetInt(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (obj.TryGetValue(k, out var v) && v is int i)
                {
                    return i;
                }
            }
            foreach (var kv in obj)
            {
                if (keys.Any(k => string.Equals(k, kv.Key, System.StringComparison.OrdinalIgnoreCase)))
                {
                    if (kv.Value is int ii)
                    {
                        return ii;
                    }
                }
            }
            return 0;
        }

        static string Unquote(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                return s.Substring(1, s.Length - 2);
            }
            return s;
        }

        var shortcut = new SteamShortcut
        {
            AppName = GetStr(Constants.AppNameKey, "AppName"),
            Exe = Unquote(GetStr(Constants.ExeKey, "Exe")),
            StartDir = GetStr(Constants.StartDirKey),
            Icon = GetStr(Constants.IconKey, "Icon"),
            ShortcutPath = GetStr(Constants.ShortcutPathKey),
            LaunchOptions = GetStr(Constants.LaunchOptionsKey),
            AppId = (uint)GetInt(Constants.AppIdKey, "AppId"),
            IsHidden = GetInt(Constants.IsHiddenKey),
            AllowDesktopConfig = GetInt(Constants.AllowDesktopConfigKey),
            AllowOverlay = GetInt(Constants.AllowOverlayKey),
            OpenVR = GetInt(Constants.OpenVRKey),
        };

        var tagsObj = obj.FirstOrDefault(kv => string.Equals(kv.Key, Constants.TagsKey, System.StringComparison.OrdinalIgnoreCase)).Value;
        if (tagsObj is Dictionary<string, object> tags)
        {
            shortcut.Tags = tags.OrderBy(k => k.Key).Select(k => k.Value?.ToString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        return shortcut;
    }

    private static Dictionary<string, object> ToObject(SteamShortcut sc)
    {
        // Ensure exe is quoted for spaces, Steam expects quoted path
        var exeOut = sc.Exe ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(exeOut))
        {
            // if not already quoted, and contains space or colon+backslash pattern, quote it
            if (!(exeOut.Length >= 2 && exeOut[0] == '"' && exeOut[exeOut.Length - 1] == '"'))
            {
                exeOut = "\"" + exeOut + "\"";
            }
        }
        var dict = new Dictionary<string, object>
        {
            [Constants.AppNameKey] = sc.AppName ?? string.Empty,
            [Constants.ExeKey] = exeOut,
            [Constants.StartDirKey] = sc.StartDir ?? string.Empty,
            [Constants.IconKey] = sc.Icon ?? string.Empty,
            [Constants.ShortcutPathKey] = sc.ShortcutPath ?? string.Empty,
            [Constants.LaunchOptionsKey] = sc.LaunchOptions ?? string.Empty,
            [Constants.AppIdKey] = unchecked((int)sc.AppId),
            [Constants.IsHiddenKey] = sc.IsHidden,
            [Constants.AllowDesktopConfigKey] = sc.AllowDesktopConfig,
            [Constants.AllowOverlayKey] = sc.AllowOverlay,
            [Constants.OpenVRKey] = sc.OpenVR,
        };

        if (sc.Tags?.Any() == true)
        {
            var tags = new Dictionary<string, object>();
            int i = 0;
            foreach (var tag in sc.Tags)
            {
                tags[i.ToString()] = tag;
                i++;
            }
            dict[Constants.TagsKey] = tags;
        }

        return dict;
    }
}
