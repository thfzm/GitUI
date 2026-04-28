using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GitUI.Models;
using GitUI.Services;
using GitUI.ViewModels;

namespace GitUI;

public partial class MainWindow : Window
{
    private bool _reallyExit;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        TrayIcon.Icon = System.Drawing.SystemIcons.Application;

        Loaded += MainWindow_Loaded;
        WatchManager.Instance.Changed += UpdateTrayState;
        UpdateTrayState();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AutoStartMenuItem.IsChecked = AutoStart.IsEnabled;

        // If launched via Windows auto-start (--minimized), go straight to tray.
        if (App.StartMinimized)
        {
            HideToTray(silent: true);
        }
    }

    // ---- Drag-drop on window for new repo creation -----------------------

    private bool IsFolderDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        return paths.Length == 1 && System.IO.Directory.Exists(paths[0]);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.IsAuthenticated && IsFolderDrop(e))
        {
            DropOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.IsAuthenticated && IsFolderDrop(e))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight)
            DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (DataContext is not MainViewModel vm || !vm.IsAuthenticated) return;
        if (!IsFolderDrop(e)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        await vm.HandleFolderDropAsync(paths[0]);
    }

    // ---- Multi-select sync -----------------------------------------------

    private void ReposListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!vm.MultiSelectMode) return;
        vm.SelectedRepos.Clear();
        foreach (var item in ReposListBox.SelectedItems.OfType<RepoItem>())
            vm.SelectedRepos.Add(item);
    }

    // ---- Window close & tray ---------------------------------------------

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_reallyExit)
        {
            base.OnClosing(e);
            return;
        }
        // X button always hides to tray so background watches keep running.
        e.Cancel = true;
        HideToTray(silent: false);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(silent: false);
        }
    }

    private void HideToTray(bool silent)
    {
        Hide();
        ShowInTaskbar = false;
        TrayIcon.Visibility = Visibility.Visible;
        UpdateTrayState();

        if (!silent && !_trayHintShown)
        {
            _trayHintShown = true;
            try
            {
                var watchCount = WatchManager.Instance.Count;
                var msg = watchCount > 0
                    ? $"GitUI는 트레이에서 계속 동작합니다.\n감시 중인 폴더: {watchCount}개"
                    : "GitUI는 트레이에서 동작합니다.\n트레이 아이콘을 더블클릭하면 다시 열립니다.";
                TrayIcon.ShowBalloonTip("GitUI", msg, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
            catch { }
        }
    }

    private void UpdateTrayState()
    {
        Dispatcher.Invoke(() =>
        {
            var n = WatchManager.Instance.Count;
            WatchCountMenuItem.Header = $"감시 중: {n}개";
            StopAllWatchesMenuItem.IsEnabled = n > 0;
            TrayIcon.ToolTipText = n > 0 ? $"GitUI · 감시 중 {n}개" : "GitUI";
        });
    }

    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e) => ShowFromTray();

    private void ShowFromTray_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        TrayIcon.Visibility = Visibility.Collapsed;
        Activate();
    }

    private void StopAllWatches_Click(object sender, RoutedEventArgs e)
    {
        WatchManager.Instance.StopAll();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (AutoStart.IsEnabled) AutoStart.Disable();
        else AutoStart.Enable();
        AutoStartMenuItem.IsChecked = AutoStart.IsEnabled;
        if (DataContext is MainViewModel vm) vm.AutoStartEnabled = AutoStart.IsEnabled;
    }

    private void ExitFromTray_Click(object sender, RoutedEventArgs e)
    {
        _reallyExit = true;
        TrayIcon.Visibility = Visibility.Collapsed;
        Application.Current.Shutdown();
    }
}
