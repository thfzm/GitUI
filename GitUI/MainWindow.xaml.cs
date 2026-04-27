using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GitUI.Models;
using GitUI.ViewModels;

namespace GitUI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TrayIcon.Icon = System.Drawing.SystemIcons.Application;
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

    // ---- Tray icon -------------------------------------------------------

    private void Window_StateChanged(object sender, System.EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            TrayIcon.Visibility = Visibility.Visible;
            ShowInTaskbar = false;
        }
    }

    private void TrayIcon_DoubleClick(object sender, System.Windows.RoutedEventArgs e)
        => ShowFromTray();

    private void ShowFromTray_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        TrayIcon.Visibility = Visibility.Collapsed;
        Activate();
    }

    private void ExitFromTray_Click(object sender, RoutedEventArgs e)
    {
        TrayIcon.Visibility = Visibility.Collapsed;
        Application.Current.Shutdown();
    }
}
