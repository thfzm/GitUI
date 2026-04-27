using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;
using GitUI.Views.Dialogs;
using ModernWpf;

namespace GitUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GitHubService _github = new();

    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string? _username;
    [ObservableProperty] private string? _avatarUrl;
    [ObservableProperty] private string? _authMethodLabel;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _filterText;
    [ObservableProperty] private RepoItem? _selectedRepo;
    [ObservableProperty] private object? _currentContent;
    [ObservableProperty] private LoginViewModel? _login;
    [ObservableProperty] private bool _multiSelectMode;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private CommandPaletteViewModel? _commandPalette;

    public ObservableCollection<RepoItem> AllRepos { get; } = new();
    public ObservableCollection<RepoItem> FilteredRepos { get; } = new();
    public ObservableCollection<RepoItem> SelectedRepos { get; } = new();

    public GitHubService GitHub => _github;

    public MainViewModel()
    {
        Login = new LoginViewModel(OnAuthenticatedAsync);
        _ = AutoLoginAsync();
    }

    private async Task AutoLoginAsync()
    {
        var saved = TokenStorage.Load();
        if (saved == null) return;
        IsBusy = true;
        try
        {
            var ok = await _github.AuthenticateAsync(saved.Token);
            if (ok) FinishLogin(saved.Method);
            else TokenStorage.Clear();
        }
        finally { IsBusy = false; }
    }

    private async Task OnAuthenticatedAsync(string token, AuthMethod method)
    {
        IsBusy = true;
        try
        {
            var ok = await _github.AuthenticateAsync(token);
            if (!ok)
            {
                if (Login != null) Login.StatusMessage = "토큰이 유효하지 않습니다.";
                return;
            }
            TokenStorage.Save(new StoredAuth(token, method, _github.Username));
            FinishLogin(method);
        }
        finally { IsBusy = false; }
    }

    private void FinishLogin(AuthMethod method)
    {
        IsAuthenticated = true;
        Username = _github.Username;
        AvatarUrl = _github.AvatarUrl;
        AuthMethodLabel = method switch
        {
            AuthMethod.OAuthWebFlow => "GitHub 로그인",
            AuthMethod.OAuthDeviceFlow => "GitHub Device",
            AuthMethod.Pat => "PAT",
            _ => null
        };
        _ = LoadReposAsync();
        ShowCreateRepo();
    }

    [RelayCommand]
    private void Logout()
    {
        TokenStorage.Clear();
        _github.Logout();
        IsAuthenticated = false;
        Username = null;
        AvatarUrl = null;
        AuthMethodLabel = null;
        AllRepos.Clear();
        FilteredRepos.Clear();
        SelectedRepos.Clear();
        SelectedRepo = null;
        CurrentContent = null;
        Login = new LoginViewModel(OnAuthenticatedAsync);
    }

    [RelayCommand]
    public async Task RefreshReposAsync() => await LoadReposAsync();

    private async Task LoadReposAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _github.GetRepositoriesAsync();
            AllRepos.Clear();
            foreach (var r in list)
            {
                AllRepos.Add(new RepoItem(r.Id, r.Name, r.FullName, r.Description,
                    r.Private, r.HtmlUrl, r.DefaultBranch ?? "main"));
            }
            ApplyFilter();
        }
        finally { IsBusy = false; }
    }

    partial void OnFilterTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredRepos.Clear();
        var q = FilterText?.Trim().ToLowerInvariant();
        foreach (var r in AllRepos)
        {
            if (string.IsNullOrEmpty(q)
                || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (r.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                FilteredRepos.Add(r);
            }
        }
    }

    [RelayCommand]
    public void ShowCreateRepo()
    {
        SelectedRepo = null;
        CurrentContent = new CreateRepoViewModel(_github, async created =>
        {
            await LoadReposAsync();
            var match = AllRepos.FirstOrDefault(r => r.Name == created.Name);
            if (match != null) SelectedRepo = match;
        });
    }

    partial void OnSelectedRepoChanged(RepoItem? value)
    {
        if (CurrentContent is RepoDetailViewModel oldVm) oldVm.Dispose();
        if (value != null)
        {
            var vm = new RepoDetailViewModel(_github, value, async () =>
            {
                await LoadReposAsync();
                ShowCreateRepo();
            });
            vm.PreviewConfirm = (preview, msg, prefix) =>
            {
                var dlg = new SyncPreviewDialog(preview, msg) { Owner = Application.Current.MainWindow };
                return Task.FromResult(dlg.ShowDialog() == true);
            };
            CurrentContent = vm;
        }
    }

    // ---- Drag folder onto window → new repo ---------------------------------

    public async Task HandleFolderDropAsync(string folderPath)
    {
        if (!System.IO.Directory.Exists(folderPath)) return;
        var dlg = new CreateRepoFromFolderDialog(_github, folderPath)
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() == true && dlg.Created != null)
        {
            await LoadReposAsync();
            var match = AllRepos.FirstOrDefault(r => r.Name == dlg.Created.Name);
            if (match != null) SelectedRepo = match;
        }
    }

    // ---- Bulk operations ----------------------------------------------------

    [RelayCommand]
    private void ToggleMultiSelect()
    {
        MultiSelectMode = !MultiSelectMode;
        if (!MultiSelectMode) SelectedRepos.Clear();
    }

    [RelayCommand]
    private async Task BulkDeleteAsync()
    {
        if (SelectedRepos.Count == 0) return;
        var names = string.Join(", ", SelectedRepos.Take(5).Select(r => r.Name));
        if (SelectedRepos.Count > 5) names += $", ... ({SelectedRepos.Count}개)";
        var result = MessageBox.Show($"다음 리포지토리를 모두 삭제하시겠습니까?\n\n{names}\n\n되돌릴 수 없습니다.",
            "일괄 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            foreach (var r in SelectedRepos.ToList())
            {
                var owner = r.FullName.Split('/')[0];
                try { await _github.DeleteRepositoryAsync(owner, r.Name); } catch { }
            }
            SelectedRepos.Clear();
            await LoadReposAsync();
            ShowCreateRepo();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task BulkArchiveAsync()
    {
        if (SelectedRepos.Count == 0) return;
        var result = MessageBox.Show($"{SelectedRepos.Count}개 리포지토리를 아카이브하시겠습니까?",
            "일괄 아카이브", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            foreach (var r in SelectedRepos.ToList())
            {
                var owner = r.FullName.Split('/')[0];
                try { await _github.SetArchivedAsync(owner, r.Name, true); } catch { }
            }
            SelectedRepos.Clear();
            await LoadReposAsync();
        }
        finally { IsBusy = false; }
    }

    // ---- Theme --------------------------------------------------------------

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ThemeManager.Current.ApplicationTheme = IsDarkTheme
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;
    }

    // ---- Command palette ----------------------------------------------------

    [RelayCommand]
    public void OpenCommandPalette()
    {
        if (!IsAuthenticated) return;
        var items = new List<CommandItem>
        {
            new("➕", "새 리포지토리", null, "Ctrl+N", () => ShowCreateRepo()),
            new("↻", "리포 목록 새로고침", null, "Ctrl+R", () => _ = LoadReposAsync()),
            new("🌓", "테마 전환 (다크/라이트)", null, null, () => ToggleTheme()),
            new("🚪", "로그아웃", null, null, () => Logout()),
            new("☑", MultiSelectMode ? "다중선택 모드 끄기" : "다중선택 모드 켜기", null, null, () => ToggleMultiSelect()),
        };
        foreach (var r in AllRepos)
        {
            var captured = r;
            items.Add(new CommandItem(
                captured.Private ? "🔒" : "📦",
                captured.Name,
                captured.Description,
                captured.FullName,
                () => SelectedRepo = captured));
        }
        CommandPalette = new CommandPaletteViewModel(items, () => CommandPalette = null);
    }
}
