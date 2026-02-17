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
    public static (bool okClicked, bool restartSteam) Show(IPlayniteAPI api, string message)
    {
        var window = api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
        window.Title = "Export Complete";
        window.Width = 500;
        window.SizeToContent = SizeToContent.Height;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var mainPanel = new StackPanel { Margin = new Thickness(20) };

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 15),
            Foreground = Brushes.White,
            FontSize = 14
        };
        mainPanel.Children.Add(textBlock);

        var actionPanel = new DockPanel
        {
            Margin = new Thickness(0, 15, 0, 0),
            LastChildFill = false
        };

        var restartCheck = new CheckBox
        {
            Content = "Restart Steam?",
            Foreground = Brushes.White,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(restartCheck, Dock.Left);
        actionPanel.Children.Add(restartCheck);

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            IsDefault = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(okButton, Dock.Right);

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
