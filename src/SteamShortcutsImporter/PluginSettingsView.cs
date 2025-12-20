using Playnite.SDK;
using System.Collections.Generic;
using System.Linq;
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
        var restoreBtn = new Button { Content = Constants.RestoreBackupLabel, Margin = new System.Windows.Thickness(0,8,0,0), MinWidth = 160 };
        restoreBtn.Click += RestoreBackupButton_Click;
        panel.Children.Add(restoreBtn);
        Content = panel;
    }

    private void RestoreBackupButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            var api = ShortcutsLibrary.GetPlayniteApiStatic();
            if (api == null)
            {
                LogManager.GetLogger().Warn("PlayniteApi not available for restore.");
                return;
            }

            // Get list of Steam users
            var userIds = ShortcutsLibrary.GetSteamUserIdsStatic();
            if (userIds.Count == 0)
            {
                api.Dialogs.ShowMessage("No Steam users found. Check your Steam path in settings.", Constants.RestoreBackupDialogTitle);
                return;
            }

            string selectedUserId;

            // If multiple users, let user pick
            if (userIds.Count > 1)
            {
                var selection = api.Dialogs.SelectString(
                    Constants.SelectSteamUserMessage,
                    Constants.SelectSteamUserTitle,
                    userIds.First()
                );

                if (!selection.Result || string.IsNullOrEmpty(selection.SelectedString))
                {
                    return; // User cancelled
                }

                // Show a selection dialog with the user IDs
                var selectedItem = api.Dialogs.ChooseItemWithSearch(
                    new List<GenericItemOption>(userIds.Select(u => new GenericItemOption(u, $"Steam User {u}"))),
                    (s) => new List<GenericItemOption>(userIds
                        .Where(u => string.IsNullOrEmpty(s) || u.Contains(s))
                        .Select(u => new GenericItemOption(u, $"Steam User {u}"))),
                    null,
                    Constants.SelectSteamUserTitle
                );

                if (selectedItem == null)
                {
                    return; // User cancelled
                }

                selectedUserId = selectedItem.Name;
            }
            else
            {
                selectedUserId = userIds.First();
            }

            // Get backup folder for this user
            var backupFolder = ShortcutsLibrary.TryGetBackupFolderForUserStatic(selectedUserId);
            if (string.IsNullOrEmpty(backupFolder))
            {
                api.Dialogs.ShowMessage(Constants.NoBackupsFoundMessage, Constants.RestoreBackupDialogTitle);
                return;
            }

            // Ensure folder exists
            System.IO.Directory.CreateDirectory(backupFolder);

            // Check if there are any backups
            var backupFiles = System.IO.Directory.GetFiles(backupFolder, Constants.BackupFileSearchPattern);
            if (backupFiles.Length == 0)
            {
                // Also check old backup location for this user
                var oldBackupFolder = ShortcutsLibrary.TryGetBackupRootStatic();
                if (!string.IsNullOrEmpty(oldBackupFolder))
                {
                    var oldShortcutsFolder = System.IO.Path.Combine(oldBackupFolder, Constants.ShortcutsKind);
                    if (System.IO.Directory.Exists(oldShortcutsFolder))
                    {
                        var oldBackups = System.IO.Directory.GetFiles(oldShortcutsFolder, $"*-{selectedUserId}-*{Constants.BackupFileExtension}");
                        if (oldBackups.Length > 0)
                        {
                            // Open old folder instead
                            backupFolder = oldShortcutsFolder;
                            backupFiles = oldBackups;
                        }
                    }
                }

                if (backupFiles.Length == 0)
                {
                    api.Dialogs.ShowMessage(Constants.NoBackupsFoundMessage, Constants.RestoreBackupDialogTitle);
                    return;
                }
            }

            // Open file dialog in the backup folder
            var selectedFile = api.Dialogs.SelectFile(Constants.BackupFileFilter + "|All Files (*.*)|*.*");
            if (string.IsNullOrEmpty(selectedFile))
            {
                return; // User cancelled
            }

            // Verify the selected file is a backup file
            if (!selectedFile.EndsWith(Constants.BackupFileExtension, System.StringComparison.OrdinalIgnoreCase))
            {
                api.Dialogs.ShowMessage("Please select a valid backup file (*.bak.vdf).", Constants.RestoreBackupDialogTitle);
                return;
            }

            // Check if Steam is running
            if (SteamProcessHelper.IsSteamRunning())
            {
                var steamResult = api.Dialogs.ShowMessage(
                    SteamProcessHelper.GetSteamRunningWarning(),
                    Constants.RestoreBackupDialogTitle,
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning
                );
                
                if (steamResult == System.Windows.MessageBoxResult.No)
                {
                    return;
                }
            }

            // Confirm restore
            var confirmResult = api.Dialogs.ShowMessage(
                Constants.RestoreConfirmMessage,
                Constants.RestoreBackupDialogTitle,
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question
            );

            if (confirmResult != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            // Perform restore
            var success = ShortcutsLibrary.RestoreBackupStatic(selectedFile, selectedUserId);
            if (success)
            {
                api.Dialogs.ShowMessage(Constants.RestoreSuccessMessage, Constants.RestoreBackupDialogTitle);
            }
            else
            {
                api.Dialogs.ShowMessage(Constants.RestoreFailedMessage, Constants.RestoreBackupDialogTitle);
            }
        }
        catch (System.Exception ex)
        {
            LogManager.GetLogger().Error(ex, "Failed to restore backup.");
        }
    }
}
