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
        string GetStr(string k) => obj.TryGetValue(k, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        int GetInt(string k) => obj.TryGetValue(k, out var v) && v is int i ? i : 0;

        var shortcut = new SteamShortcut
        {
            AppName = GetStr("appname"),
            Exe = GetStr("exe"),
            StartDir = GetStr("StartDir"),
            Icon = GetStr("icon"),
            ShortcutPath = GetStr("ShortcutPath"),
            LaunchOptions = GetStr("LaunchOptions"),
            IsHidden = GetInt("IsHidden"),
            AllowDesktopConfig = GetInt("AllowDesktopConfig"),
            AllowOverlay = GetInt("AllowOverlay"),
            OpenVR = GetInt("OpenVR"),
        };

        if (obj.TryGetValue("tags", out var tagsObj) && tagsObj is Dictionary<string, object> tags)
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
}

internal static class BinaryKv
{
    private const byte End = 0x00;
    private const byte String = 0x01;
    private const byte Int32 = 0x02;
    private const byte Object = 0x08;

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

