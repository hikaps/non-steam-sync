using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
        if (root.TryGetValue("shortcuts", out var shortcutsNode) && shortcutsNode is Dictionary<string, object> shortcuts)
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
        root["shortcuts"] = shortcutsObj;

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
                if (keys.Any(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase)))
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
                if (keys.Any(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase)))
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
            AppName = GetStr("appname", "AppName"),
            Exe = Unquote(GetStr("exe", "Exe")),
            StartDir = GetStr("StartDir"),
            Icon = GetStr("icon", "Icon"),
            ShortcutPath = GetStr("ShortcutPath"),
            LaunchOptions = GetStr("LaunchOptions"),
            AppId = (uint)GetInt("appid", "AppId"),
            IsHidden = GetInt("IsHidden"),
            AllowDesktopConfig = GetInt("AllowDesktopConfig"),
            AllowOverlay = GetInt("AllowOverlay"),
            OpenVR = GetInt("OpenVR"),
        };

        var tagsObj = obj.FirstOrDefault(kv => string.Equals(kv.Key, "tags", StringComparison.OrdinalIgnoreCase)).Value;
        if (tagsObj is Dictionary<string, object> tags)
        {
            shortcut.Tags = tags.OrderBy(k => k.Key).Select(k => k.Value?.ToString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        return shortcut;
    }

    private static Dictionary<string, object> ToObject(SteamShortcut sc)
    {
        var dict = new Dictionary<string, object>
        {
            ["appname"] = sc.AppName ?? string.Empty,
            ["exe"] = sc.Exe ?? string.Empty,
            ["StartDir"] = sc.StartDir ?? string.Empty,
            ["icon"] = sc.Icon ?? string.Empty,
            ["ShortcutPath"] = sc.ShortcutPath ?? string.Empty,
            ["LaunchOptions"] = sc.LaunchOptions ?? string.Empty,
            ["appid"] = unchecked((int)sc.AppId),
            ["IsHidden"] = sc.IsHidden,
            ["AllowDesktopConfig"] = sc.AllowDesktopConfig,
            ["AllowOverlay"] = sc.AllowOverlay,
            ["OpenVR"] = sc.OpenVR,
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
            dict["tags"] = tags;
        }

        return dict;
    }
}

internal static class Utils
{
    public static string HashString(string input)
    {
        unchecked
        {
            // Simple FNV-1a 64-bit, hex string
            const ulong fnvOffset = 1469598103934665603;
            const ulong fnvPrime = 1099511628211;
            ulong hash = fnvOffset;
            foreach (var b in Encoding.UTF8.GetBytes(input))
            {
                hash ^= b;
                hash *= fnvPrime;
            }
            return hash.ToString("x16");
        }
    }

    public static uint Crc32(byte[] data)
    {
        unchecked
        {
            uint[] table = Crc32Table;
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                byte index = (byte)((crc ^ data[i]) & 0xFF);
                crc = (crc >> 8) ^ table[index];
            }
            return ~crc;
        }
    }

    public static uint GenerateShortcutAppId(string exe, string appName)
    {
        // Common approach used by community tools; Steam ORs with 0x80000000 for shortcuts
        var seed = (exe ?? string.Empty) + (appName ?? string.Empty);
        var crc = Crc32(Encoding.UTF8.GetBytes(seed));
        return crc | 0x80000000u;
    }

    public static ulong ToShortcutGameId( uint appId )
    {
        // 64-bit game id for shortcuts: (appid << 32) | 0x02000000
        return ((ulong)appId << 32) | 0x02000000UL;
    }

    private static uint[]? _crcTable;
    private static uint[] Crc32Table => _crcTable ??= BuildCrc32Table();
    private static uint[] BuildCrc32Table()
    {
        const uint poly = 0xEDB88320;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                if ((c & 1) != 0)
                {
                    c = poly ^ (c >> 1);
                }
                else
                {
                    c >>= 1;
                }
            }
            table[i] = c;
        }
        return table;
    }
}

internal static class BinaryKv
{
    // Binary KeyValues used by Steam shortcuts.vdf
    // 0x00 = object (node), 0x08 = end of object
    private const byte Object = 0x00;
    private const byte String = 0x01;
    private const byte Int32 = 0x02;
    private const byte End = 0x08;

    public static Dictionary<string, object> ReadObject(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return ReadObject(br);
    }

    private static Dictionary<string, object> ReadObject(BinaryReader br)
    {
        var obj = new Dictionary<string, object>(StringComparer.Ordinal);
        while (true)
        {
            byte t = br.ReadByte();
            if (t == End)
            {
                break;
            }

            string key = ReadCString(br);

            switch (t)
            {
                case String:
                    obj[key] = ReadCString(br);
                    break;
                case Int32:
                    obj[key] = br.ReadInt32();
                    break;
                case Object:
                    obj[key] = ReadObject(br);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported KV type: 0x{t:x2}");
            }
        }
        return obj;
    }

    public static void WriteObject(Stream stream, Dictionary<string, object> obj)
    {
        using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteObject(bw, obj);
    }

    private static void WriteObject(BinaryWriter bw, Dictionary<string, object> obj)
    {
        foreach (var kv in obj)
        {
            if (kv.Value is Dictionary<string, object> child)
            {
                bw.Write(Object);
                WriteCString(bw, kv.Key);
                WriteObject(bw, child);
            }
            else if (kv.Value is int i)
            {
                bw.Write(Int32);
                WriteCString(bw, kv.Key);
                bw.Write(i);
            }
            else
            {
                bw.Write(String);
                WriteCString(bw, kv.Key);
                WriteCString(bw, kv.Value?.ToString() ?? string.Empty);
            }
        }
        bw.Write(End);
    }

    private static string ReadCString(BinaryReader br)
    {
        var bytes = new List<byte>(32);
        while (true)
        {
            byte b = br.ReadByte();
            if (b == 0) break;
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void WriteCString(BinaryWriter bw, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        bw.Write(bytes);
        bw.Write((byte)0);
    }
}
