using Playnite.SDK;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SteamShortcutsImporter;

/// <summary>
/// Dialog displayed after export completes, offering to restart Steam.
/// </summary>
public static class ExportCompletionDialog
{
    public static (bool okClicked, bool restartSteam) Show(IPlayniteAPI api, string message, string? steamRootPath)
    {
        var window = api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
        window.Title = "Export Complete";
        window.Width = 450;
        window.Height = 250;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var mainPanel = new StackPanel { Margin = new Thickness(20) };

        bool steamIsRunning = SteamProcessHelper.IsSteamRunning();
        string displayMessage = steamIsRunning ? message + " (Steam is currently running)" : message;

        var textBlock = new TextBlock
        {
            Text = displayMessage,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 5),
            Foreground = Brushes.White,
            FontSize = 14
        };
        mainPanel.Children.Add(textBlock);

        var restartCheck = new CheckBox
        {
            Content = steamIsRunning ? "Launch Steam again?" : "Restart Steam after sync?",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = Brushes.White,
            FontSize = 14
        };

        bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        bool hasSteamExe = !string.IsNullOrEmpty(steamRootPath) && File.Exists(Path.Combine(steamRootPath, "steam.exe"));

        if (!isWindows)
        {
            restartCheck.IsEnabled = false;
            restartCheck.Visibility = Visibility.Collapsed;
        }
        else if (!hasSteamExe)
        {
            restartCheck.IsEnabled = false;
            restartCheck.ToolTip = "Steam executable not found in the configured Steam path.";
        }

        mainPanel.Children.Add(restartCheck);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 0, 0)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            IsDefault = true
        };

        okButton.Click += (s, e) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        buttonPanel.Children.Add(okButton);
        mainPanel.Children.Add(buttonPanel);

        window.Content = mainPanel;

        var result = window.ShowDialog();

        return (result == true, result == true && restartCheck.IsChecked == true);
    }
}
