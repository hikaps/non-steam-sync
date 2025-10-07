
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SteamShortcutsImporter;

public class PluginSettings : ISettings
{
    private readonly LibraryPlugin? _plugin;

    public string SteamRootPath { get; set; } = string.Empty;
    public bool LaunchViaSteam { get; set; } = true;
    public Dictionary<string, string> ExportMap { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void BeginEdit() { }
    public void CancelEdit() { }
    public void EndEdit()
    {
        if (_plugin != null)
        {
            _plugin.SavePluginSettings(this);
        }
    }

    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SteamRootPath))
        {
            errors.Add("Steam library path is required.");
        }
        return errors.Count == 0;
    }

    public PluginSettings() { }

    public PluginSettings(LibraryPlugin plugin)
    {
        _plugin = plugin;
        try
        {
            var saved = plugin.LoadPluginSettings<PluginSettings>();
            if (saved != null)
            {
                SteamRootPath = saved.SteamRootPath;
                LaunchViaSteam = saved.LaunchViaSteam;
                ExportMap = saved.ExportMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                SteamRootPath = GuessSteamRootPath() ?? string.Empty;
                LaunchViaSteam = true;
                ExportMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Error(ex, "Failed to load saved settings, falling back to defaults.");
            SteamRootPath = GuessSteamRootPath() ?? string.Empty;
            LaunchViaSteam = true;
            ExportMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? GuessSteamRootPath()
    {
        try
        {
            var regPath = Registry.CurrentUser.OpenSubKey(Constants.SteamRegistryPath)?.GetValue(Constants.SteamPathRegistryValue) as string;
            if (!string.IsNullOrWhiteSpace(regPath) && Directory.Exists(regPath))
            {
                return regPath;
            }

            var pf86 = Environment.GetEnvironmentVariable(Constants.ProgramFilesX86EnvVar);
            var pf = Environment.GetEnvironmentVariable(Constants.ProgramFilesEnvVar);
            var local = Environment.GetEnvironmentVariable(Constants.LocalAppDataEnvVar);

            var candidates = new[]
            {
                pf86 != null ? Path.Combine(pf86, "Steam") : null,
                pf != null ? Path.Combine(pf, "Steam") : null,
                local != null ? Path.Combine(local, "Steam") : null
            };

            foreach (var c in candidates.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                if (Directory.Exists(c!))
                {
                    return c!;
                }
            }

            var fallback1 = Constants.DefaultSteamPathX86;
            if (Directory.Exists(fallback1))
            {
                return fallback1;
            }
            var fallback2 = Constants.DefaultSteamPath;
            if (Directory.Exists(fallback2))
            {
                return fallback2;
            }
        }
        catch (Exception ex)
        {
            LogManager.GetLogger().Warn(ex, "Failed to guess Steam root path.");
        }
        return null;
    }
}
