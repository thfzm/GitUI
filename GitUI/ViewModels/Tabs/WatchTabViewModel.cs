using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.ViewModels.Tabs;

public partial class WatchTabViewModel : ObservableObject, IDisposable
{
    private readonly GitHubService _github;
    private readonly FolderWatcherService _watcher = new();
    public RepoItem Repo { get; }
    public Func<string> CurrentBranchAccessor { get; }

    [ObservableProperty] private string? _selectedFolder;
    [ObservableProperty] private string _commitMessage = "Auto-sync via GitUI";
    [ObservableProperty] private string _targetSubpath = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _debounceSeconds = 3;

    public ObservableCollection<string> EventLog { get; } = new();

    public WatchTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch)
    {
        _github = github;
        Repo = repo;
        CurrentBranchAccessor = currentBranch;
        _watcher.FileChanged += OnFileChanged;
        _watcher.ChangesSettled += OnChangesSettled;
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
        if (IsRunning) { StopWatch(); }
        else { StartWatch(); }
    }

    private void StartWatch()
    {
        if (string.IsNullOrEmpty(SelectedFolder) || !Directory.Exists(SelectedFolder))
        {
            StatusMessage = "유효한 폴더를 선택하세요.";
            return;
        }
        try
        {
            _watcher.DebounceMilliseconds = Math.Max(1, DebounceSeconds) * 1000;
            _watcher.Start(SelectedFolder!);
            IsRunning = true;
            AppendLog($"[감시 시작] {SelectedFolder}");
            StatusMessage = $"파일 변경 감지 시 {DebounceSeconds}초 후 자동 푸시.";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
    }

    private void StopWatch()
    {
        _watcher.Stop();
        IsRunning = false;
        AppendLog("[감시 중지]");
        StatusMessage = null;
    }

    private void OnFileChanged(string path)
    {
        try
        {
            var rel = Path.GetRelativePath(SelectedFolder!, path);
            Application.Current.Dispatcher.Invoke(() => AppendLog($"변경: {rel}"));
        }
        catch { }
    }

    private async void OnChangesSettled()
    {
        if (IsBusy) return;
        await Application.Current.Dispatcher.InvokeAsync(() => AppendLog("[디바운스 완료, 동기화 시작]"));
        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var prefix = TargetSubpath?.Trim().Trim('/') ?? "";
            var msg = string.IsNullOrWhiteSpace(CommitMessage) ? "Auto-sync via GitUI" : CommitMessage;
            var preview = await SyncPreview.ComputeAsync(_github.Client!, owner, Repo.Name, CurrentBranchAccessor(), SelectedFolder!, prefix);
            int n = 0;
            foreach (var e in preview.Entries)
            {
                if (e.Change is not (FileChange.Added or FileChange.Modified)) continue;
                var content = await File.ReadAllBytesAsync(e.LocalPath);
                await _github.UploadFileAsync(owner, Repo.Name, e.TargetPath, content, msg, CurrentBranchAccessor());
                n++;
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
                AppendLog(n > 0 ? $"[동기화 완료] {n}개 파일" : "[변경 없음]"));
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => AppendLog($"[오류] {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLog(string line)
    {
        EventLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (EventLog.Count > 200) EventLog.RemoveAt(EventLog.Count - 1);
    }

    public void Dispose() => _watcher.Dispose();
}
