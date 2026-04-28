using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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

    private bool _suppressSave;
    private bool _folderHealthChecked;

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
        _suppressSave = true;
        try
        {
            var existing = WatchManager.Instance.Get(Repo.FullName);
            if (existing != null)
            {
                // Active watch wins — show its live config & log.
                IsRunning = true;
                SelectedFolder = existing.Config.FolderPath;
                CommitMessage = existing.Config.CommitMessage;
                TargetSubpath = existing.Config.TargetSubpath;
                DebounceSeconds = existing.Config.DebounceSeconds;
                RespectGitignore = existing.Config.RespectGitignore;
                MirrorDeletions = existing.Config.MirrorDeletions;
                EventLog = existing.Log;
                StatusMessage = $"감시 중 · 디바운스 {DebounceSeconds}초 · 창을 닫아도 계속 동작합니다";
            }
            else
            {
                // Not watching — restore last preferences from per-repo settings.
                IsRunning = false;
                var p = RepoSettingsStore.Get(Repo.FullName);
                SelectedFolder = p.WatchFolder;
                TargetSubpath = p.WatchTargetSubpath ?? "";
                CommitMessage = string.IsNullOrEmpty(p.WatchCommitMessage)
                    ? "Auto-sync via GitUI" : p.WatchCommitMessage;
                DebounceSeconds = p.WatchDebounceSeconds;
                RespectGitignore = p.WatchRespectGitignore;
                MirrorDeletions = p.WatchMirrorDeletions;
                EventLog = new ObservableCollection<string>();
                StatusMessage = null;
            }
        }
        finally { _suppressSave = false; }
    }

    private void SavePrefs()
    {
        if (_suppressSave) return;
        // Don't overwrite the active watch's config with the editable VM state —
        // active state lives in WatchManager and persists separately.
        if (IsRunning) return;
        var p = RepoSettingsStore.Get(Repo.FullName);
        p.WatchFolder = SelectedFolder;
        p.WatchTargetSubpath = TargetSubpath;
        p.WatchCommitMessage = CommitMessage;
        p.WatchDebounceSeconds = DebounceSeconds;
        p.WatchRespectGitignore = RespectGitignore;
        p.WatchMirrorDeletions = MirrorDeletions;
        RepoSettingsStore.Save();
    }

    partial void OnSelectedFolderChanged(string? value) => SavePrefs();
    partial void OnTargetSubpathChanged(string value) => SavePrefs();
    partial void OnCommitMessageChanged(string value) => SavePrefs();
    partial void OnRespectGitignoreChanged(bool value) => SavePrefs();
    partial void OnMirrorDeletionsChanged(bool value) => SavePrefs();
    partial void OnDebounceSecondsChanged(int value) => SavePrefs();

    /// <summary>
    /// Called by the View on Loaded. Prompts the user once if the saved folder is gone.
    /// Skips the prompt when a watch is actively running (the watch survived → folder was OK).
    /// </summary>
    public void VerifyFolderHealth()
    {
        if (_folderHealthChecked) return;
        _folderHealthChecked = true;
        if (IsRunning) return;

        var folder = SelectedFolder;
        if (string.IsNullOrEmpty(folder)) return;
        if (Directory.Exists(folder)) return;

        var result = MessageBox.Show(
            $"이 리포지토리에 저장된 감시 폴더를 찾을 수 없습니다.\n\n경로: {folder}\n\n새 폴더를 선택하시겠습니까?",
            $"{Repo.Name} · 감시 폴더 없음",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            PickFolder();
        else if (result == MessageBoxResult.No)
            SelectedFolder = null;
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
            RespectGitignore: RespectGitignore,
            MirrorDeletions: MirrorDeletions);

        WatchManager.Instance.Start(config);
    }

    public void Dispose()
    {
        WatchManager.Instance.Changed -= OnManagerChanged;
    }
}
