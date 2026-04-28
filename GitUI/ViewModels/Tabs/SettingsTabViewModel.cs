using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;
using Microsoft.Win32;

namespace GitUI.ViewModels.Tabs;

public partial class ReleaseAttachment : ObservableObject
{
    public string LocalPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public long Size { get; init; }
    public bool IsTempZip { get; init; }
    public string DisplaySize =>
        Size < 1024 ? $"{Size} B" :
        Size < 1024 * 1024 ? $"{Size / 1024.0:0.#} KB" :
        $"{Size / 1024.0 / 1024.0:0.#} MB";
}

public partial class SettingsTabViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly Func<Task> _onRepoChanged;
    public RepoItem Repo { get; }
    public Func<string> CurrentBranchAccessor { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isPrivate;
    [ObservableProperty] private bool _pagesEnabled;
    [ObservableProperty] private string? _pagesUrl;
    [ObservableProperty] private bool _loaded;

    [ObservableProperty] private string _releaseTag = "";
    [ObservableProperty] private string _releaseName = "";
    [ObservableProperty] private string _releaseBody = "";
    [ObservableProperty] private bool _releasePrerelease;

    public ObservableCollection<ReleaseAttachment> Attachments { get; } = new();

    public SettingsTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch, Func<Task> onRepoChanged)
    {
        _github = github;
        Repo = repo;
        CurrentBranchAccessor = currentBranch;
        _onRepoChanged = onRepoChanged;
        IsPrivate = repo.Private;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var status = await _github.GetPagesStatusAsync(owner, Repo.Name);
            PagesEnabled = status.enabled;
            PagesUrl = status.url;
            Loaded = true;
            await SuggestNextTagAsync();
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleVisibilityAsync()
    {
        var newPrivate = !IsPrivate;
        var confirm = MessageBox.Show(
            $"리포지토리를 {(newPrivate ? "Private" : "Public")}로 변경하시겠습니까?",
            "공개여부 변경", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            await _github.SetVisibilityAsync(owner, Repo.Name, newPrivate);
            IsPrivate = newPrivate;
            StatusMessage = newPrivate ? "Private로 변경됨." : "Public으로 변경됨.";
            await _onRepoChanged();
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ArchiveAsync()
    {
        var confirm = MessageBox.Show(
            "이 리포지토리를 아카이브하시겠습니까? 읽기 전용으로 변경됩니다.",
            "아카이브", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            await _github.SetArchivedAsync(owner, Repo.Name, true);
            StatusMessage = "아카이브 완료.";
            await _onRepoChanged();
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task TogglePagesAsync()
    {
        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            if (PagesEnabled)
            {
                await _github.DisablePagesAsync(owner, Repo.Name);
                PagesEnabled = false;
                PagesUrl = null;
                StatusMessage = "Pages 비활성화 완료.";
            }
            else
            {
                await _github.EnablePagesAsync(owner, Repo.Name, CurrentBranchAccessor());
                var status = await _github.GetPagesStatusAsync(owner, Repo.Name);
                PagesEnabled = status.enabled;
                PagesUrl = status.url;
                StatusMessage = "Pages 활성화 완료.";
            }
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenPagesUrl()
    {
        if (string.IsNullOrEmpty(PagesUrl)) return;
        try { Process.Start(new ProcessStartInfo { FileName = PagesUrl, UseShellExecute = true }); }
        catch { }
    }

    // ---- Releases -----------------------------------------------------------

    [RelayCommand]
    private async Task SuggestNextTagAsync()
    {
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var latest = await _github.GetLatestReleaseTagAsync(owner, Repo.Name);
            if (string.IsNullOrEmpty(latest))
            {
                ReleaseTag = "v0.1.0";
                return;
            }
            ReleaseTag = BumpPatch(latest);
        }
        catch { }
    }

    private static string BumpPatch(string tag)
    {
        var m = Regex.Match(tag, @"^(?<prefix>v?)(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>.*)$");
        if (!m.Success) return tag + ".1";
        var patch = int.Parse(m.Groups["patch"].Value) + 1;
        return $"{m.Groups["prefix"].Value}{m.Groups["major"].Value}.{m.Groups["minor"].Value}.{patch}";
    }

    [RelayCommand]
    private async Task GenerateReleaseNotesAsync()
    {
        if (string.IsNullOrWhiteSpace(ReleaseTag))
        {
            StatusMessage = "먼저 태그 이름을 입력하세요.";
            return;
        }
        IsBusy = true;
        StatusMessage = "릴리스 노트 생성 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var previous = await _github.GetLatestReleaseTagAsync(owner, Repo.Name);
            var (name, body) = await _github.GenerateReleaseNotesAsync(
                owner, Repo.Name, ReleaseTag.Trim(), previous, CurrentBranchAccessor());
            if (string.IsNullOrWhiteSpace(ReleaseName)) ReleaseName = name;
            ReleaseBody = body;
            StatusMessage = "노트가 자동 생성되었습니다. 필요하면 수정하세요.";
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void AddAttachmentFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "첨부할 파일 선택",
            Multiselect = true,
            Filter = "All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var path in dlg.FileNames)
        {
            var info = new FileInfo(path);
            Attachments.Add(new ReleaseAttachment
            {
                LocalPath = path,
                FileName = info.Name,
                Size = info.Length,
                IsTempZip = false
            });
        }
    }

    [RelayCommand]
    private void AddAttachmentFolder()
    {
        var dlg = new OpenFolderDialog { Title = "ZIP으로 첨부할 폴더 선택" };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var folder = dlg.FolderName;
            var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            var tempZip = Path.Combine(Path.GetTempPath(), $"GitUI-asset-{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(folder, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            var size = new FileInfo(tempZip).Length;
            Attachments.Add(new ReleaseAttachment
            {
                LocalPath = tempZip,
                FileName = $"{folderName}.zip",
                Size = size,
                IsTempZip = true
            });
            StatusMessage = $"폴더 '{folderName}'을(를) ZIP으로 압축했습니다.";
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void RemoveAttachment(ReleaseAttachment? item)
    {
        if (item == null) return;
        if (item.IsTempZip)
        {
            try { File.Delete(item.LocalPath); } catch { }
        }
        Attachments.Remove(item);
    }

    [RelayCommand]
    private async Task CreateReleaseAsync()
    {
        if (string.IsNullOrWhiteSpace(ReleaseTag))
        {
            StatusMessage = "태그 이름이 필요합니다.";
            return;
        }
        IsBusy = true;
        StatusMessage = "릴리스 생성 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var release = await _github.CreateReleaseAsync(
                owner, Repo.Name,
                ReleaseTag.Trim(),
                string.IsNullOrWhiteSpace(ReleaseName) ? null : ReleaseName.Trim(),
                string.IsNullOrWhiteSpace(ReleaseBody) ? null : ReleaseBody,
                ReleasePrerelease,
                CurrentBranchAccessor());

            // Upload attachments
            for (int i = 0; i < Attachments.Count; i++)
            {
                var att = Attachments[i];
                StatusMessage = $"첨부 업로드 중 ({i + 1}/{Attachments.Count})... {att.FileName}";
                var bytes = await File.ReadAllBytesAsync(att.LocalPath);
                await _github.UploadReleaseAssetAsync(release, att.FileName, bytes);
            }

            // Cleanup temp zips
            foreach (var att in Attachments)
            {
                if (att.IsTempZip)
                {
                    try { File.Delete(att.LocalPath); } catch { }
                }
            }
            Attachments.Clear();

            StatusMessage = $"릴리스 '{release.TagName}' 생성됨.";
            ReleaseTag = "";
            ReleaseName = "";
            ReleaseBody = "";
            try { Process.Start(new ProcessStartInfo { FileName = release.HtmlUrl, UseShellExecute = true }); } catch { }
            await SuggestNextTagAsync();
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
