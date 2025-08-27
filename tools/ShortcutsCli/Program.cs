using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SteamShortcutsImporter;

internal class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            switch (args[0])
            {
                case "read":
                    return CmdRead(args.Skip(1).ToArray());
                case "write-sample":
                    return CmdWriteSample(args.Skip(1).ToArray());
                case "roundtrip":
                    return CmdRoundtrip(args.Skip(1).ToArray());
                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}\n{ex}");
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Steam Shortcuts CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  read <shortcuts.vdf>");
        Console.WriteLine("  write-sample <out.vdf>");
        Console.WriteLine("  roundtrip <in.vdf> <out.vdf>");
    }

    private static int CmdRead(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("read requires a path");
            return 1;
        }
        var path = args[0];
        var items = ShortcutsFile.Read(path).ToList();
        Console.WriteLine($"Read {items.Count} shortcuts from {path}");
        foreach (var s in items)
        {
            Console.WriteLine($"- {s.AppName}");
            Console.WriteLine($"  exe: {s.Exe}");
            Console.WriteLine($"  dir: {s.StartDir}");
            Console.WriteLine($"  args: {s.LaunchOptions}");
            if (s.Tags?.Any() == true)
            {
                Console.WriteLine($"  tags: {string.Join(", ", s.Tags)}");
            }
        }
        return 0;
    }

    private static int CmdWriteSample(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("write-sample requires an output path");
            return 1;
        }
        var outPath = args[0];

        var sample = new List<SteamShortcut>
        {
            new SteamShortcut
            {
                AppName = "My Game",
                Exe = "C:/Games/MyGame/MyGame.exe",
                StartDir = "C:/Games/MyGame",
                LaunchOptions = "-windowed",
                Tags = new List<string>{"Action","Indie"},
                AllowOverlay = 1,
                AllowDesktopConfig = 1,
                OpenVR = 0
            },
            new SteamShortcut
            {
                AppName = "Emulator Title",
                Exe = "C:/Emu/emu.exe",
                StartDir = "C:/Emu",
                LaunchOptions = "--rom C:/Roms/title.rom",
                Tags = new List<string>{"Emulator"}
            }
        };

        ShortcutsFile.Write(outPath, sample);
        Console.WriteLine($"Wrote sample to {outPath}");
        return 0;
    }

    private static int CmdRoundtrip(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("roundtrip requires input and output paths");
            return 1;
        }
        var inPath = args[0];
        var outPath = args[1];

        var items = ShortcutsFile.Read(inPath).ToList();
        ShortcutsFile.Write(outPath, items);
        Console.WriteLine($"Roundtripped {items.Count} entries from {inPath} -> {outPath}");
        Console.WriteLine("Note: binary output ordering may differ; content should be equivalent.");
        return 0;
    }
}

