using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class BinaryKvTests
{
    [Fact]
    public void ReadsEmptyObject()
    {
        var ms = new MemoryStream();
        ms.WriteByte(0x08); // End marker
        ms.Position = 0;
        
        var obj = BinaryKv.ReadObject(ms);
        
        Assert.Empty(obj);
    }

    [Fact]
    public void WritesEmptyObject()
    {
        var obj = new Dictionary<string, object>();
        var ms = new MemoryStream();
        
        BinaryKv.WriteObject(ms, obj);
        
        ms.Position = 0;
        Assert.Equal(0x08, ms.ReadByte()); // Should just be end marker
        Assert.Equal(-1, ms.ReadByte()); // EOF
    }

    [Fact]
    public void RoundTripsSimpleString()
    {
        var original = new Dictionary<string, object>
        {
            ["test"] = "value"
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Single(result);
        Assert.Equal("value", result["test"]);
    }

    [Fact]
    public void RoundTripsSimpleInt()
    {
        var original = new Dictionary<string, object>
        {
            ["number"] = 42
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Single(result);
        Assert.Equal(42, result["number"]);
    }

    [Fact]
    public void RoundTripsNestedObject()
    {
        var original = new Dictionary<string, object>
        {
            ["outer"] = new Dictionary<string, object>
            {
                ["inner"] = "nested value"
            }
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Single(result);
        Assert.IsType<Dictionary<string, object>>(result["outer"]);
        var inner = (Dictionary<string, object>)result["outer"];
        Assert.Equal("nested value", inner["inner"]);
    }

    [Fact]
    public void RoundTripsDeeplyNestedStructure()
    {
        // Create 5-level deep structure
        var level5 = new Dictionary<string, object> { ["value"] = "deep" };
        var level4 = new Dictionary<string, object> { ["level5"] = level5 };
        var level3 = new Dictionary<string, object> { ["level4"] = level4 };
        var level2 = new Dictionary<string, object> { ["level3"] = level3 };
        var original = new Dictionary<string, object> { ["level2"] = level2 };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        // Navigate down
        var r2 = (Dictionary<string, object>)result["level2"];
        var r3 = (Dictionary<string, object>)r2["level3"];
        var r4 = (Dictionary<string, object>)r3["level4"];
        var r5 = (Dictionary<string, object>)r4["level5"];
        
        Assert.Equal("deep", r5["value"]);
    }

    [Fact]
    public void HandlesUnicodeStrings()
    {
        var original = new Dictionary<string, object>
        {
            ["游戏"] = "ゲーム",  // Chinese/Japanese characters
            ["emoji"] = "🎮🕹️",
            ["cyrillic"] = "Игра"
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Equal("ゲーム", result["游戏"]);
        Assert.Equal("🎮🕹️", result["emoji"]);
        Assert.Equal("Игра", result["cyrillic"]);
    }

    [Fact]
    public void HandlesEmptyStrings()
    {
        var original = new Dictionary<string, object>
        {
            ["empty"] = "",
            ["key2"] = "value"
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Equal("", result["empty"]);
        Assert.Equal("value", result["key2"]);
    }

    [Fact]
    public void HandlesNegativeIntegers()
    {
        var original = new Dictionary<string, object>
        {
            ["negative"] = -42,
            ["zero"] = 0,
            ["positive"] = 100
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Equal(-42, result["negative"]);
        Assert.Equal(0, result["zero"]);
        Assert.Equal(100, result["positive"]);
    }

    [Fact]
    public void HandlesMixedTypes()
    {
        var original = new Dictionary<string, object>
        {
            ["string"] = "text",
            ["number"] = 123,
            ["nested"] = new Dictionary<string, object>
            {
                ["inner_string"] = "inner",
                ["inner_number"] = 456
            }
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Equal(3, result.Count);
        Assert.Equal("text", result["string"]);
        Assert.Equal(123, result["number"]);
        var nested = (Dictionary<string, object>)result["nested"];
        Assert.Equal("inner", nested["inner_string"]);
        Assert.Equal(456, nested["inner_number"]);
    }

    [Fact]
    public void ThrowsOnInvalidTypeCode()
    {
        var ms = new MemoryStream();
        ms.WriteByte(0xFF); // Invalid type code
        WriteCString(ms, "key");
        ms.Position = 0;
        
        Assert.Throws<InvalidDataException>(() => BinaryKv.ReadObject(ms));
    }

    [Fact]
    public void HandlesLargeNumberOfEntries()
    {
        // Create object with 100 entries
        var original = new Dictionary<string, object>();
        for (int i = 0; i < 100; i++)
        {
            original[$"key_{i}"] = $"value_{i}";
        }
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Equal(100, result.Count);
        Assert.Equal("value_50", result["key_50"]);
    }

    [Fact]
    public void HandlesVeryLongStrings()
    {
        var longString = new string('x', 10000);
        var original = new Dictionary<string, object>
        {
            ["long"] = longString
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        Assert.Equal(longString, result["long"]);
    }

    [Fact]
    public void PreservesKeyOrder()
    {
        var original = new Dictionary<string, object>
        {
            ["zebra"] = "z",
            ["alpha"] = "a",
            ["middle"] = "m"
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        // Dictionary maintains insertion order in .NET Core+
        var keys = result.Keys.ToList();
        Assert.Equal(3, keys.Count);
        // All keys should be present regardless of order
        Assert.Contains("zebra", keys);
        Assert.Contains("alpha", keys);
        Assert.Contains("middle", keys);
    }

    [Fact]
    public void CoercesNonIntegersToString()
    {
        // BinaryKv only supports string, int, and nested objects
        // Anything else should be coerced to string
        var original = new Dictionary<string, object>
        {
            ["bool_true"] = true,
            ["bool_false"] = false,
            ["long"] = 123456789012345L
        };
        
        var ms = new MemoryStream();
        BinaryKv.WriteObject(ms, original);
        ms.Position = 0;
        var result = BinaryKv.ReadObject(ms);
        
        // These become strings during write (ToString())
        Assert.Equal("True", result["bool_true"]);
        Assert.Equal("False", result["bool_false"]);
        Assert.Equal("123456789012345", result["long"]);
    }

    // Helper method to write C-style null-terminated string
    private void WriteCString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0); // Null terminator
    }
}
