using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.Views.Dialogs;

public partial class CloneDialog : Window
{
    private readonly CloneService _clone;
    private readonly RepoItem _repo;
    private CancellationTokenSource? _cts;
    private string? _resultPath;

    public CloneDialog(CloneService cloneService, RepoItem repo, System.Collections.Generic.IEnumerable<string> branches, string defaultBranch)
    {
        _clone = cloneService;
        _repo = repo;
        InitializeComponent();
        RepoLabel.Text = repo.FullName;

        var defaultParent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GitHub");
        PathBox.Text = Path.Combine(defaultParent, repo.Name);

        foreach (var b in branches) BranchBox.Items.Add(b);
        BranchBox.SelectedItem = defaultBranch;
        if (BranchBox.Items.Count == 0)
        {
            BranchBox.Items.Add(defaultBranch);
            BranchBox.SelectedIndex = 0;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "클론할 부모 폴더 선택",
            DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog() == true)
        {
            PathBox.Text = Path.Combine(dlg.FolderName, _repo.Name);
        }
    }

    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            StatusLabel.Text = "경로를 입력하세요.";
            return;
        }
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            var c = MessageBox.Show("대상 폴더가 비어있지 않습니다. 그래도 진행하시겠습니까?",
                "경고", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (c != MessageBoxResult.Yes) return;
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var url = $"https://github.com/{_repo.FullName}.git";
        var branch = BranchBox.SelectedItem?.ToString();

        var progress = new Progress<CloneProgress>(p =>
        {
            Progress.Value = p.Percent;
            StatusLabel.Text = p.Message;
        });

        try
        {
            await _clone.CloneAsync(url, path, branch, RecursiveCheck.IsChecked == true, progress, _cts.Token);
            _resultPath = path;
            StatusLabel.Text = $"✓ 완료: {path}";
            Progress.Value = 100;
            CloneButton.Visibility = Visibility.Collapsed;
            OpenButton.Visibility = Visibility.Visible;
            CancelButton.Content = "닫기";
            BrowseButton.IsEnabled = false;
            PathBox.IsEnabled = false;
            BranchBox.IsEnabled = false;
            RecursiveCheck.IsEnabled = false;

            if (OpenAfterCheck.IsChecked == true)
            {
                OpenFolder_Click(this, null!);
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "오류: " + ex.Message;
            SetBusy(false);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_resultPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _resultPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch { }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = !string.IsNullOrEmpty(_resultPath);
        Close();
    }

    private void SetBusy(bool busy)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CloneButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        PathBox.IsEnabled = !busy;
        BranchBox.IsEnabled = !busy;
        RecursiveCheck.IsEnabled = !busy;
        OpenAfterCheck.IsEnabled = !busy;
    }
}
