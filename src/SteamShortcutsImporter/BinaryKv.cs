
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SteamShortcutsImporter;

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
        var obj = new Dictionary<string, object>(System.StringComparer.Ordinal);
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
