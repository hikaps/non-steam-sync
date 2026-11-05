using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SteamShortcutsImporter;

/// <summary>
/// Builds and displays selection dialogs for importing/exporting games.
/// </summary>
internal sealed class SelectionDialogBuilder
{
    private readonly IPlayniteAPI playniteApi;
    private readonly ILogger logger;
    private readonly string pluginName;

    public SelectionDialogBuilder(IPlayniteAPI playniteApi, ILogger logger, string pluginName)
    {
        this.playniteApi = playniteApi;
        this.logger = logger;
        this.pluginName = pluginName;
    }

    public void ShowSelectionDialog<T>(
        string title,
        List<T> items,
        Func<T, string> displayText,
        Func<T, string?> previewImage,
        Func<T, bool> isInitiallyChecked,
        Func<T, bool> isNew,
        string confirmLabel,
        Action<List<T>> onConfirm)
    {
        var window = CreateSelectionWindow(title);
        var (topBar, searchBar, cbOnlyNew, statusText, btnSelectAll, btnSelectNone, btnInvert) = CreateTopBar();
        var (listBox, contentPanel) = CreateMainContent(topBar);
        var (bottomBar, btnConfirm, btnCancel) = CreateBottomBar(confirmLabel);

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        System.Windows.Controls.Grid.SetRow(contentPanel, 0);
        grid.Children.Add(contentPanel);
        System.Windows.Controls.Grid.SetRow(bottomBar, 1);
        grid.Children.Add(bottomBar);

        window.Content = grid;

        var checks = new List<System.Windows.Controls.CheckBox>();

        void UpdateStatus()
        {
            int selected = checks.Count(c => c.IsChecked == true);
            int total = checks.Count;
            statusText.Text = string.Format(Constants.StatusTextFormat, selected, total);
        }

        void Refresh()
        {
            RefreshList(items, displayText, previewImage, isInitiallyChecked, isNew, searchBar.Text, cbOnlyNew.IsChecked, listBox, checks, UpdateStatus);
        }

        searchBar.TextChanged += (_, __) => Refresh();
        cbOnlyNew.Checked += (_, __) => Refresh();
        cbOnlyNew.Unchecked += (_, __) => Refresh();

        btnSelectAll.Click += (_, __) =>
        {
            foreach (var cb in checks) { cb.IsChecked = true; }
            UpdateStatus();
        };
        btnSelectNone.Click += (_, __) =>
        {
            foreach (var cb in checks) { cb.IsChecked = false; }
            UpdateStatus();
        };
        btnInvert.Click += (_, __) =>
        {
            foreach (var cb in checks) { cb.IsChecked = !cb.IsChecked; }
            UpdateStatus();
        };

        Refresh();

        btnConfirm.Click += (_, __) =>
        {
            try
            {
                var selected = checks.Where(c => c.IsChecked == true).Select(c => (T)c.Tag).ToList();
                onConfirm(selected);
                window.DialogResult = true; window.Close();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Selection dialog confirm failed");
                playniteApi.Dialogs.ShowErrorMessage($"Failed: {ex.Message}", pluginName);
            }
        };

        btnCancel.Click += (_, __) => { window.DialogResult = false; window.Close(); };

        window.ShowDialog();
    }

    private System.Windows.Window CreateSelectionWindow(string title)
    {
        var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions { ShowCloseButton = true });
        window.Title = title;
        window.Width = 900;
        window.Height = 650;
        window.MinWidth = 720;
        window.MinHeight = 480;
        return window;
    }

    private (System.Windows.Controls.StackPanel, System.Windows.Controls.TextBox, System.Windows.Controls.CheckBox, System.Windows.Controls.TextBlock, System.Windows.Controls.Button, System.Windows.Controls.Button, System.Windows.Controls.Button) CreateTopBar()
    {
        var topBar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 12, 12, 6) };
        var lblFilter = new System.Windows.Controls.TextBlock { Text = Constants.FilterLabel, Margin = new System.Windows.Thickness(0, 0, 8, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
        var searchBar = new System.Windows.Controls.TextBox { Width = 320, Margin = new System.Windows.Thickness(0, 0, 16, 0) };
        var btnSelectAll = new System.Windows.Controls.Button { Content = Constants.SelectAllLabel, Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 100 };
        var btnSelectNone = new System.Windows.Controls.Button { Content = Constants.DeselectAllLabel, MinWidth = 100 };
        var btnInvert = new System.Windows.Controls.Button { Content = Constants.InvertLabel, Margin = new System.Windows.Thickness(8, 0, 0, 0), MinWidth = 80 };
        var cbOnlyNew = new System.Windows.Controls.CheckBox { Content = Constants.OnlyNewLabel, Margin = new System.Windows.Thickness(12, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
        topBar.Children.Add(lblFilter);
        topBar.Children.Add(searchBar);
        topBar.Children.Add(btnSelectAll);
        topBar.Children.Add(btnSelectNone);
        topBar.Children.Add(btnInvert);
        topBar.Children.Add(cbOnlyNew);
        var statusText = new System.Windows.Controls.TextBlock { Margin = new System.Windows.Thickness(16, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
        topBar.Children.Add(statusText);
        return (topBar, searchBar, cbOnlyNew, statusText, btnSelectAll, btnSelectNone, btnInvert);
    }

    private (System.Windows.Controls.ListBox, System.Windows.Controls.Grid) CreateMainContent(System.Windows.Controls.StackPanel topBar)
    {
        var contentGrid = new System.Windows.Controls.Grid();
        contentGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        contentGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        System.Windows.Controls.Grid.SetRow(topBar, 0);
        contentGrid.Children.Add(topBar);
        var listBox = new System.Windows.Controls.ListBox
        {
            Margin = new System.Windows.Thickness(12, 0, 12, 0),
            Background = Brushes.Transparent,
            BorderThickness = new System.Windows.Thickness(0),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch
        };
        System.Windows.Controls.VirtualizingStackPanel.SetIsVirtualizing(listBox, true);
        System.Windows.Controls.ScrollViewer.SetCanContentScroll(listBox, true);
        System.Windows.Controls.Grid.SetRow(listBox, 1);
        contentGrid.Children.Add(listBox);
        return (listBox, contentGrid);
    }

    private (System.Windows.Controls.StackPanel, System.Windows.Controls.Button, System.Windows.Controls.Button) CreateBottomBar(string confirmLabel)
    {
        var bottom = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(12, 6, 12, 12), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var btnConfirm = new System.Windows.Controls.Button { Content = confirmLabel, Margin = new System.Windows.Thickness(0, 0, 8, 0), MinWidth = 150 };
        var btnCancel = new System.Windows.Controls.Button { Content = Constants.CancelLabel, MinWidth = 100 };
        bottom.Children.Add(btnConfirm);
        bottom.Children.Add(btnCancel);
        return (bottom, btnConfirm, btnCancel);
    }

    private void RefreshList<T>(List<T> items, Func<T, string> displayText, Func<T, string?> previewImage, Func<T, bool> isInitiallyChecked, Func<T, bool> isNew, string? filter, bool? onlyNew, System.Windows.Controls.ListBox listBox, List<System.Windows.Controls.CheckBox> checks, Action updateStatus)
    {
        listBox.Items.Clear();
        checks.Clear();

        foreach (var it in items)
        {
            var name = displayText(it) ?? string.Empty;
            if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            if (onlyNew == true && !isNew(it))
            {
                continue;
            }
            var cb = new System.Windows.Controls.CheckBox { Content = BuildListItemWithPreview(name, previewImage(it)), IsChecked = isInitiallyChecked(it), Tag = it, Margin = new System.Windows.Thickness(0, 4, 0, 4) };
            cb.Checked += (_, __) => updateStatus();
            cb.Unchecked += (_, __) => updateStatus();
            checks.Add(cb);
            listBox.Items.Add(cb);
        }
        updateStatus();
    }

    private object BuildListItemWithPreview(string text, string? imagePath)
    {
        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath);
                bmp.DecodePixelWidth = 96;
                bmp.EndInit();
                var img = new System.Windows.Controls.Image
                {
                    Width = 48,
                    Height = 48,
                    Margin = new System.Windows.Thickness(0, 0, 8, 0),
                    Source = bmp
                };
                System.Windows.Controls.Grid.SetColumn(img, 0);
                grid.Children.Add(img);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to build list item with preview.");
            }
        }

        var tb = new System.Windows.Controls.TextBlock
        {
            Text = text,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
            TextWrapping = System.Windows.TextWrapping.NoWrap
        };
        System.Windows.Controls.Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        return grid;
    }
}
