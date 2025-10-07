
using System.Text;

namespace SteamShortcutsImporter;

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
