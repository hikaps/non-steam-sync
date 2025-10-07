using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class ParserTests
{
    [Fact]
    public void RoundTripsSampleVdf()
    {
        var tmp = Path.Combine(Path.GetTempPath(),
            "shortcuts_" + Guid.NewGuid().ToString("N") + ".vdf");

        try
        {
            var sample = new[]
            {
                new SteamShortcut
                {
                    AppName = "Sample Game",
                    Exe = "C:/Games/Sample/Sample.exe",
                    StartDir = "C:/Games/Sample",
                    LaunchOptions = "-windowed",
                    Tags = new System.Collections.Generic.List<string>{"Action"}
                }
            };

            ShortcutsFile.Write(tmp, sample);
            Assert.True(File.Exists(tmp));

            var items = ShortcutsFile.Read(tmp).ToList();
            Assert.True(items.Count == 1);

            var first = items.First();
            Assert.Equal("Sample Game", first.AppName);
            Assert.Contains("Sample.exe", first.Exe);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }
}
