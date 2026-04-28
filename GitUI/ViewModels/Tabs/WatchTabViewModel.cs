using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.ViewModels.Tabs;

public partial class WatchTabViewModel : ObservableObject, IDisposable
{
    private readonly GitHubService _github;
    public RepoItem Repo { get; }
    public Func<string> CurrentBranchAccessor { get; }

    [ObservableProperty] private string? _selectedFolder;
    [ObservableProperty] private string _commitMessage = "Auto-sync via GitUI";
    [ObservableProperty] private string _targetSubpath = "";
    [ObservableProperty] private bool _respectGitignore = true;
    [ObservableProperty] private bool _mirrorDeletions = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _debounceSeconds = 3;
    [ObservableProperty] private ObservableCollection<string> _eventLog = new();

    public WatchTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch)
    {
        _github = github;
        Repo = repo;
        CurrentBranchAccessor = currentBranch;
        WatchManager.Instance.Changed += OnManagerChanged;
        ReloadFromManager();
    }

    private void OnManagerChanged() => ReloadFromManager();

    private void ReloadFromManager()
    {
        var existing = WatchManager.Instance.Get(Repo.FullName);
        if (existing != null)
        {
            IsRunning = true;
            SelectedFolder = existing.Config.FolderPath;
            CommitMessage = existing.Config.CommitMessage;
            TargetSubpath = existing.Config.TargetSubpath;
            DebounceSeconds = existing.Config.DebounceSeconds;
            RespectGitignore = existing.Config.RespectGitignore;
            EventLog = existing.Log;
            StatusMessage = $"감시 중 · 디바운스 {DebounceSeconds}초 · 창을 닫아도 계속 동작합니다";
        }
        else
        {
            IsRunning = false;
            // Keep last entered values; clear log since it's per-instance.
            EventLog = new ObservableCollection<string>();
            StatusMessage = null;
        }
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "감시할 폴더 선택" };
        if (dlg.ShowDialog() == true)
            SelectedFolder = dlg.FolderName;
    }

    [RelayCommand]
    private void ToggleWatch()
    {
        if (IsRunning)
        {
            WatchManager.Instance.Stop(Repo.FullName);
            return;
        }

        if (string.IsNullOrEmpty(SelectedFolder) || !Directory.Exists(SelectedFolder))
        {
            StatusMessage = "유효한 폴더를 선택하세요.";
            return;
        }

        var config = new WatchConfig(
            Id: Repo.FullName,
            RepoFullName: Repo.FullName,
            DefaultBranch: CurrentBranchAccessor(),
            FolderPath: SelectedFolder!,
            CommitMessage: string.IsNullOrWhiteSpace(CommitMessage) ? "Auto-sync via GitUI" : CommitMessage,
            TargetSubpath: TargetSubpath ?? "",
            DebounceSeconds: Math.Max(1, DebounceSeconds),
            RespectGitignore: RespectGitignore);

        WatchManager.Instance.Start(config);
    }

    public void Dispose()
    {
        WatchManager.Instance.Changed -= OnManagerChanged;
    }
}
