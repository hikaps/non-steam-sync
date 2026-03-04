using Playnite.SDK;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SteamShortcutsImporter;

internal static class EmulatorPathUtils
{
    public static bool IsRegexPattern(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        var value = s ?? string.Empty;
        return value.StartsWith("^") || value.EndsWith("$") || value.Contains("\\.") || value.Contains(".*");
    }

    public static string QuoteArgumentsIfNeeded(string? args)
    {
        if (args == null)
        {
            return string.Empty;
        }

        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || !trimmed.Contains(" "))
        {
            return trimmed;
        }

        if (trimmed.Contains(" --") || trimmed.Contains(" -"))
        {
            return trimmed;
        }

        return "\"" + trimmed + "\"";
    }

    public static string? ResolveExecutablePattern(string pattern, string? emulatorDir, ILogger logger, string gameName)
    {
        if (string.IsNullOrEmpty(emulatorDir) || !Directory.Exists(emulatorDir))
        {
            logger.Warn($"Cannot resolve emulator pattern for '{gameName}': Emulator directory not found ({emulatorDir})");
            return null;
        }

        try
        {
            var regexPattern = pattern;
            if (!regexPattern.StartsWith("^"))
            {
                regexPattern = "^" + regexPattern;
            }

            if (!regexPattern.EndsWith("$"))
            {
                regexPattern += "$";
            }

            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            foreach (var file in Directory.EnumerateFiles(emulatorDir, "*.exe", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (regex.IsMatch(fileName))
                {
                    logger.Info($"Resolved emulator pattern '{pattern}' to '{file}' for '{gameName}'");
                    return file;
                }
            }

            logger.Warn($"Cannot export '{gameName}': No executable matching pattern '{pattern}' found in {emulatorDir}");
        }
        catch (Exception ex)
        {
            logger.Warn(ex, $"Failed to resolve emulator pattern '{pattern}' for '{gameName}'");
        }

        return null;
    }
}
