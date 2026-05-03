using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.ViewModels.Tabs;

public partial class UploadTabViewModel : ObservableObject
{
    private readonly GitHubService _github;
    public RepoItem Repo { get; }
    public Func<string> CurrentBranchAccessor { get; }

    public Func<SyncPreviewResult, string, string, Task<bool>>? PreviewConfirm { get; set; }

    [ObservableProperty] private string? _selectedFolder;
    [ObservableProperty] private string _commitMessage = "Update via GitUI";
    [ObservableProperty] private string _targetSubpath = "";
    [ObservableProperty] private bool _respectGitignore = true;
    [ObservableProperty] private bool _mirrorDeletions = true;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _progressLabel;

    private bool _suppressSave;
    private bool _folderHealthChecked;

    public UploadTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch)
    {
        _github = github;
        Repo = repo;
        CurrentBranchAccessor = currentBranch;
        LoadFromPrefs();
    }

    private void LoadFromPrefs()
    {
        _suppressSave = true;
        try
        {
            var p = RepoSettingsStore.Get(Repo.FullName);
            SelectedFolder = p.UploadFolder;
            TargetSubpath = p.UploadTargetSubpath ?? "";
            CommitMessage = string.IsNullOrEmpty(p.UploadCommitMessage) ? "Update via GitUI" : p.UploadCommitMessage;
            RespectGitignore = p.UploadRespectGitignore;
            MirrorDeletions = p.UploadMirrorDeletions;
        }
        finally { _suppressSave = false; }
    }

    private void SavePrefs()
    {
        if (_suppressSave) return;
        var p = RepoSettingsStore.Get(Repo.FullName);
        p.UploadFolder = SelectedFolder;
        p.UploadTargetSubpath = TargetSubpath;
        p.UploadCommitMessage = CommitMessage;
        p.UploadRespectGitignore = RespectGitignore;
        p.UploadMirrorDeletions = MirrorDeletions;
        RepoSettingsStore.Save();
    }

    partial void OnSelectedFolderChanged(string? value) => SavePrefs();
    partial void OnTargetSubpathChanged(string value) => SavePrefs();
    partial void OnCommitMessageChanged(string value) => SavePrefs();
    partial void OnRespectGitignoreChanged(bool value) => SavePrefs();
    partial void OnMirrorDeletionsChanged(bool value) => SavePrefs();

    /// <summary>
    /// Called from the View's Loaded handler. If the previously-saved upload folder
    /// no longer exists on disk, prompt the user once to pick a new one.
    /// </summary>
    public void VerifyFolderHealth()
    {
        if (_folderHealthChecked) return;
        _folderHealthChecked = true;

        var folder = SelectedFolder;
        if (string.IsNullOrEmpty(folder)) return;
        if (Directory.Exists(folder)) return;

        var result = MessageBox.Show(
            $"이 리포지토리에 저장된 업로드 폴더를 찾을 수 없습니다.\n\n경로: {folder}\n\n새 폴더를 선택하시겠습니까?",
            $"{Repo.Name} · 업로드 폴더 없음",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            PickFolder();
        }
        else if (result == MessageBoxResult.No)
        {
            // Forget the saved value so we don't pester next time.
            SelectedFolder = null;
        }
        // Cancel → leave it alone; will ask again next session.
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "동기화할 폴더 선택" };
        if (dlg.ShowDialog() == true)
            SelectedFolder = dlg.FolderName;
    }

    [RelayCommand]
    private async Task PreviewSyncAsync()
    {
        if (string.IsNullOrEmpty(SelectedFolder) || !Directory.Exists(SelectedFolder))
        {
            StatusMessage = "유효한 폴더를 선택하세요.";
            return;
        }
        if (PreviewConfirm == null) { await SyncFolderAsync(); return; }

        IsBusy = true;
        StatusMessage = "변경 분석 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var prefix = TargetSubpath?.Trim().Trim('/') ?? "";
            var preview = await SyncPreview.ComputeAsync(_github.Client!, owner, Repo.Name, CurrentBranchAccessor(), SelectedFolder!, prefix);
            var msg = string.IsNullOrWhiteSpace(CommitMessage) ? "Update via GitUI" : CommitMessage;
            var confirmed = await PreviewConfirm(preview, msg, prefix);
            if (confirmed) await ExecuteSyncAsync(preview, msg);
            else StatusMessage = "취소됨.";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SyncFolderAsync()
    {
        if (string.IsNullOrEmpty(SelectedFolder) || !Directory.Exists(SelectedFolder))
        {
            StatusMessage = "유효한 폴더를 선택하세요.";
            return;
        }
        var owner = Repo.FullName.Split('/')[0];
        var msg = string.IsNullOrWhiteSpace(CommitMessage) ? "Update via GitUI" : CommitMessage;
        var prefix = TargetSubpath?.Trim().Trim('/') ?? "";

        IsBusy = true;
        StatusMessage = $"'{Path.GetFileName(SelectedFolder)}' 동기화 중...";
        try
        {
            // Use SyncPreview so we can also process deletions (mirror sync) when MirrorDeletions is on.
            var preview = await SyncPreview.ComputeAsync(_github.Client!, owner, Repo.Name,
                CurrentBranchAccessor(), SelectedFolder!, prefix);
            await ExecuteSyncAsync(preview, msg);
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            Progress = 0;
            ProgressLabel = null;
            IsBusy = false;
        }
    }

    public async Task ExecuteSyncAsync(SyncPreviewResult preview, string message)
    {
        var owner = Repo.FullName.Split('/')[0];
        IsBusy = true;
        try
        {
            var upserts = new List<(string path, byte[] content)>();
            var deletes = new List<string>();
            foreach (var e in preview.Entries)
            {
                if (e.Change is FileChange.Added or FileChange.Modified)
                {
                    var bytes = await File.ReadAllBytesAsync(e.LocalPath);
                    upserts.Add((e.TargetPath, bytes));
                }
                else if (e.Change == FileChange.Deleted && MirrorDeletions)
                {
                    deletes.Add(e.TargetPath);
                }
            }

            if (upserts.Count == 0 && deletes.Count == 0)
            {
                StatusMessage = "변경 사항 없음.";
                return;
            }

            ProgressLabel = $"0/{upserts.Count} · blob 업로드 중...";
            var prog = new Progress<(int current, int total, string filename)>(t =>
            {
                Progress = (double)t.current / Math.Max(1, t.total) * 100;
                ProgressLabel = $"{t.current}/{t.total} · {t.filename}";
            });

            await _github.BulkCommitAsync(owner, Repo.Name, CurrentBranchAccessor(),
                message, upserts, deletes, prog,
                notify: m => Application.Current?.Dispatcher.Invoke(() => StatusMessage = m));

            StatusMessage = $"동기화 완료 (1커밋) — 업로드 {upserts.Count} · 삭제 {deletes.Count} · 동일 {preview.Unchanged} · 스킵 {preview.Skipped}";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            Progress = 0;
            ProgressLabel = null;
            IsBusy = false;
        }
    }

    public async Task UploadDroppedAsync(string[] paths)
    {
        if (paths == null || paths.Length == 0) return;
        var owner = Repo.FullName.Split('/')[0];
        var msg = string.IsNullOrWhiteSpace(CommitMessage) ? "Upload via GitUI" : CommitMessage;
        var prefix = TargetSubpath?.Trim().Trim('/') ?? "";

        IsBusy = true;
        StatusMessage = "업로드 중...";
        try
        {
            var work = new List<(string src, string target)>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    var folder = p.TrimEnd(Path.DirectorySeparatorChar);
                    var folderName = Path.GetFileName(folder);
                    var matcher = RespectGitignore ? GitignoreMatcher.LoadFrom(folder) : new GitignoreMatcher(folder, Array.Empty<string>());
                    foreach (var f in GitHubService.EnumerateFiles(folder, matcher))
                    {
                        var rel = Path.GetRelativePath(folder, f).Replace('\\', '/');
                        var target = $"{folderName}/{rel}";
                        if (!string.IsNullOrEmpty(prefix)) target = $"{prefix}/{target}";
                        work.Add((f, target));
                    }
                }
                else if (File.Exists(p))
                {
                    var name = Path.GetFileName(p);
                    var target = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
                    work.Add((p, target));
                }
            }

            var upserts = new List<(string path, byte[] content)>(work.Count);
            foreach (var (src, target) in work)
            {
                var content = await File.ReadAllBytesAsync(src);
                upserts.Add((target, content));
            }

            ProgressLabel = $"0/{upserts.Count} · blob 업로드 중...";
            var prog = new Progress<(int current, int total, string filename)>(t =>
            {
                Progress = (double)t.current / Math.Max(1, t.total) * 100;
                ProgressLabel = $"{t.current}/{t.total} · {t.filename}";
            });

            await _github.BulkCommitAsync(owner, Repo.Name, CurrentBranchAccessor(),
                msg, upserts, Array.Empty<string>(), prog,
                notify: m => Application.Current?.Dispatcher.Invoke(() => StatusMessage = m));

            StatusMessage = $"{upserts.Count}개 파일 업로드 완료 (1커밋).";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            Progress = 0;
            ProgressLabel = null;
            IsBusy = false;
        }
    }

    public async Task UploadClipboardImageAsync(byte[] pngBytes)
    {
        var owner = Repo.FullName.Split('/')[0];
        IsBusy = true;
        StatusMessage = "스크린샷 업로드 중...";
        try
        {
            var msg = string.IsNullOrWhiteSpace(CommitMessage) ? "Add screenshot via GitUI" : CommitMessage;
            await _github.UploadImageAsync(owner, Repo.Name, pngBytes, msg, CurrentBranchAccessor());
            StatusMessage = "스크린샷이 screenshots/ 에 업로드되었습니다.";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
