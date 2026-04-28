using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;
using GitUI.ViewModels.Tabs;
using Octokit;

namespace GitUI.ViewModels;

public partial class RepoDetailViewModel : ObservableObject, IDisposable
{
    private readonly GitHubService _github;
    private readonly Func<Task> _onRepoChanged;

    public RepoItem Repo { get; }
    public UploadTabViewModel Upload { get; }
    public WatchTabViewModel Watch { get; }
    public FilesTabViewModel Files { get; }
    public ReadmeTabViewModel Readme { get; }
    public HistoryTabViewModel History { get; }
    public IssuesTabViewModel Issues { get; }
    public SettingsTabViewModel Settings { get; }

    [ObservableProperty] private string _currentBranch;
    [ObservableProperty] private string _newBranchName = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<string> Branches { get; } = new();

    /// <summary>
    /// View hosts can register a confirmation callback to display a sync preview dialog.
    /// </summary>
    public Func<SyncPreviewResult, string, string, Task<bool>>? PreviewConfirm
    {
        get => Upload.PreviewConfirm;
        set => Upload.PreviewConfirm = value;
    }

    public RepoDetailViewModel(GitHubService github, RepoItem repo, Func<Task> onRepoChanged)
    {
        _github = github;
        Repo = repo;
        _onRepoChanged = onRepoChanged;
        _currentBranch = repo.DefaultBranch;
        Branches.Add(repo.DefaultBranch);

        Func<string> branchAccessor = () => CurrentBranch;
        Upload = new UploadTabViewModel(github, repo, branchAccessor);
        Watch = new WatchTabViewModel(github, repo, branchAccessor);
        Files = new FilesTabViewModel(github, repo, branchAccessor);
        Readme = new ReadmeTabViewModel(github, repo, branchAccessor);
        History = new HistoryTabViewModel(github, repo, branchAccessor);
        Issues = new IssuesTabViewModel(github, repo);
        Settings = new SettingsTabViewModel(github, repo, branchAccessor, onRepoChanged);

        _ = LoadBranchesAsync();
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        try { Process.Start(new ProcessStartInfo { FileName = Repo.HtmlUrl, UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void Clone()
    {
        var clone = _github.CreateCloneService();
        if (clone == null) return;
        var dlg = new GitUI.Views.Dialogs.CloneDialog(clone, Repo, Branches, CurrentBranch)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dlg.ShowDialog();
    }

    [RelayCommand]
    private void CopyCloneUrl()
    {
        try
        {
            Clipboard.SetText($"https://github.com/{Repo.FullName}.git");
            StatusMessage = "Clone URL이 클립보드에 복사되었습니다.";
        }
        catch { }
    }

    [RelayCommand]
    private async Task DeleteRepoAsync()
    {
        var result = MessageBox.Show(
            $"'{Repo.FullName}' 리포지토리를 정말 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
            "리포지토리 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            await _github.DeleteRepositoryAsync(owner, Repo.Name);
            await _onRepoChanged();
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task LoadBranchesAsync()
    {
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var branches = await _github.GetBranchesAsync(owner, Repo.Name);
            Branches.Clear();
            foreach (var b in branches) Branches.Add(b.Name);
            if (!Branches.Contains(CurrentBranch) && Branches.Count > 0)
                CurrentBranch = Branches[0];
        }
        catch { /* empty repo or no branches yet */ }
    }

    [RelayCommand]
    private async Task RefreshBranchesAsync() => await LoadBranchesAsync();

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewBranchName))
        {
            StatusMessage = "브랜치 이름을 입력하세요.";
            return;
        }
        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            await _github.CreateBranchAsync(owner, Repo.Name, NewBranchName.Trim(), CurrentBranch);
            StatusMessage = $"브랜치 '{NewBranchName}' 생성됨.";
            var created = NewBranchName.Trim();
            NewBranchName = "";
            await LoadBranchesAsync();
            CurrentBranch = created;
        }
        catch (Exception ex) { StatusMessage = "오류: " + ex.Message; }
        finally { IsBusy = false; }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        // Lazy-load tab content. Tab order: 0 Upload, 1 Watch, 2 Files, 3 README, 4 History, 5 Issues, 6 Settings
        switch (value)
        {
            case 2 when !Files.Loaded: _ = Files.LoadTreeAsync(); break;
            case 3 when !Readme.Loaded: _ = Readme.LoadAsync(); break;
            case 4 when !History.Loaded: _ = History.LoadAsync(); break;
            case 5 when !Issues.Loaded: _ = Issues.LoadAsync(); break;
            case 6 when !Settings.Loaded: _ = Settings.LoadAsync(); break;
        }
    }

    /// <summary>Drag-drop handler — delegates to upload tab.</summary>
    public Task UploadDroppedAsync(string[] paths) => Upload.UploadDroppedAsync(paths);

    /// <summary>Ctrl+V handler — delegates to upload tab.</summary>
    public Task UploadClipboardImageAsync(byte[] pngBytes) => Upload.UploadClipboardImageAsync(pngBytes);

    public void Dispose() => Watch.Dispose();
}
