using Playnite.SDK;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SteamShortcutsImporter;

/// <summary>
/// Dialog displayed after export completes, offering to restart Steam.
/// </summary>
public static class ExportCompletionDialog
{
    // Playnite theme resource key for text foreground color
    private const string TextBrushKey = "TextBrush";

    /// <summary>
    /// Gets a theme-aware brush for text, falling back to system colors if Playnite theme is unavailable.
    /// </summary>
    private static Brush GetThemeAwareTextBrush()
    {
        // Try to get Playnite's theme text brush
        if (Application.Current?.TryFindResource(TextBrushKey) is Brush themeBrush)
        {
            return themeBrush;
        }
        // Fallback to system control text color (adapts to OS theme)
        return SystemColors.ControlTextBrush;
    }

    public static (bool okClicked, bool restartSteam) Show(IPlayniteAPI api, string message)
    {
        var window = api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
        window.Title = "Export Complete";
        window.Width = 500;
        window.SizeToContent = SizeToContent.Height;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var textBrush = GetThemeAwareTextBrush();
        var mainPanel = new StackPanel { Margin = new Thickness(20) };

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 15),
            Foreground = textBrush,
            FontSize = 14
        };
        mainPanel.Children.Add(textBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 15, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        var restartCheck = new CheckBox
        {
            Content = "Restart Steam?",
            Margin = new Thickness(0, 0, 15, 0),
            Foreground = textBrush,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionPanel.Children.Add(restartCheck);

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        okButton.Click += (s, e) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        actionPanel.Children.Add(okButton);
        mainPanel.Children.Add(actionPanel);

        window.Content = mainPanel;

        var result = window.ShowDialog();

        return (result == true, result == true && restartCheck.IsChecked == true);
    }
}
