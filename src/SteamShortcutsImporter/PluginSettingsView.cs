using Playnite.SDK;
using System.Windows.Controls;

namespace SteamShortcutsImporter;

public class PluginSettingsView : UserControl
{
    public PluginSettingsView()
    {
        var pathBox = new TextBox { Name = Constants.SteamRootPathBoxName, MinWidth = 400 };
        pathBox.SetBinding(TextBox.TextProperty,
            new System.Windows.Data.Binding("SteamRootPath") { Mode = System.Windows.Data.BindingMode.TwoWay });

        var panel = new StackPanel { Margin = new System.Windows.Thickness(12) };
        panel.Children.Add(new TextBlock { Text = @"Steam library path (e.g., C:\Program Files (x86)\Steam):", FontWeight = System.Windows.FontWeights.Bold, FontSize = 11 });
        pathBox.Margin = new System.Windows.Thickness(0, 4, 0, 0);
        panel.Children.Add(pathBox);
        var launchCheck = new CheckBox { Content = "Launch via Steam (rungameid) when possible", Margin = new System.Windows.Thickness(0,8,0,0) };
        launchCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding("LaunchViaSteam") { Mode = System.Windows.Data.BindingMode.TwoWay });
        panel.Children.Add(launchCheck);
        var backupBtn = new Button { Content = Constants.OpenBackupFolderLabel, Margin = new System.Windows.Thickness(0,8,0,0), MinWidth = 160 };
        backupBtn.Click += (_, __) =>
        {
            try
            {
                var libDataDir = ShortcutsLibrary.TryGetBackupRootStatic();
                if (!string.IsNullOrEmpty(libDataDir))
                {
                    System.IO.Directory.CreateDirectory(libDataDir);
                    var psi = new System.Diagnostics.ProcessStartInfo { FileName = libDataDir, UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (System.Exception ex)
            {
                LogManager.GetLogger().Warn(ex, "Failed to open backup folder.");
            }
        };
        panel.Children.Add(backupBtn);
        Content = panel;
    }
}
