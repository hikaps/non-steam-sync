using Playnite.SDK;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SteamShortcutsImporter;

public static class ExportCompletionDialog
{
    private const string TextBrushKey = "TextBrush";

    private static Brush GetThemeAwareTextBrush()
    {
        if (Application.Current?.TryFindResource(TextBrushKey) is Brush themeBrush)
        {
            return themeBrush;
        }
        return SystemColors.ControlTextBrush;
    }

    public static void Show(IPlayniteAPI api, string message)
    {
        var window = api.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
        window.Title = Constants.ExportCompleteTitle;
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

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        okButton.Click += (s, e) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        mainPanel.Children.Add(okButton);
        window.Content = mainPanel;
        window.ShowDialog();
    }
}
