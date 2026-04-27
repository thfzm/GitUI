using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.ViewModels.Tabs;

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
            StatusMessage = $"릴리스 '{release.TagName}' 생성됨.";
            ReleaseTag = "";
            ReleaseName = "";
            ReleaseBody = "";
            try { Process.Start(new ProcessStartInfo { FileName = release.HtmlUrl, UseShellExecute = true }); } catch { }
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
