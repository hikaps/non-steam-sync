using System.IO;
using System.Linq;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class ParserTests
{
    [Fact]
    public void ReadsSampleVdf()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "shortcuts.vdf");
        Assert.True(File.Exists(path));
        var items = ShortcutsFile.Read(path).ToList();
        Assert.True(items.Count > 0);

        var first = items.First();
        Assert.False(string.IsNullOrWhiteSpace(first.AppName));
        Assert.False(string.IsNullOrWhiteSpace(first.Exe));
        // AppId may be 0 if not present, but parser should not throw
    }
}

