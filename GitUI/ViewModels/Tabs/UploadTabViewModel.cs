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

    public UploadTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch)
    {
        _github = github;
        Repo = repo;
        CurrentBranchAccessor = currentBranch;
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
            var work = new List<SyncFileEntry>();
            foreach (var e in preview.Entries)
            {
                if (e.Change is FileChange.Added or FileChange.Modified) work.Add(e);
                else if (e.Change == FileChange.Deleted && MirrorDeletions) work.Add(e);
            }

            int uploaded = 0, removed = 0;
            for (int i = 0; i < work.Count; i++)
            {
                var e = work[i];
                Progress = (double)(i + 1) / work.Count * 100;
                if (e.Change == FileChange.Deleted)
                {
                    ProgressLabel = $"{i + 1}/{work.Count} · 🗑 {e.TargetPath}";
                    await _github.DeleteFileAsync(owner, Repo.Name, e.TargetPath, e.RemoteSha, message, CurrentBranchAccessor());
                    removed++;
                }
                else
                {
                    ProgressLabel = $"{i + 1}/{work.Count} · {e.TargetPath}";
                    var content = await File.ReadAllBytesAsync(e.LocalPath);
                    await _github.UploadFileAsync(owner, Repo.Name, e.TargetPath, content, message, CurrentBranchAccessor());
                    uploaded++;
                }
            }
            StatusMessage = $"동기화 완료 — 업로드 {uploaded} · 삭제 {removed} · 동일 {preview.Unchanged} · 스킵 {preview.Skipped}";
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

            for (int i = 0; i < work.Count; i++)
            {
                var (src, target) = work[i];
                Progress = (double)(i + 1) / work.Count * 100;
                ProgressLabel = $"{i + 1}/{work.Count} · {target}";
                var content = await File.ReadAllBytesAsync(src);
                await _github.UploadFileAsync(owner, Repo.Name, target, content, msg, CurrentBranchAccessor());
            }

            StatusMessage = $"{work.Count}개 파일 업로드 완료.";
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
