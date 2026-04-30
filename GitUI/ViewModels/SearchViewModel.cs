using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.ViewModels;

public record SearchResultItem(
    string FullName,
    string Name,
    string Owner,
    string AvatarUrl,
    string? Description,
    int Stars,
    int Forks,
    string? Language,
    DateTime UpdatedAt,
    string HtmlUrl,
    string DefaultBranch,
    bool IsPrivate)
{
    public string StarsDisplay =>
        Stars >= 1_000_000 ? $"{Stars / 1_000_000.0:0.#}M" :
        Stars >= 1_000 ? $"{Stars / 1_000.0:0.#}k" :
        Stars.ToString();

    public string ForksDisplay =>
        Forks >= 1_000_000 ? $"{Forks / 1_000_000.0:0.#}M" :
        Forks >= 1_000 ? $"{Forks / 1_000.0:0.#}k" :
        Forks.ToString();

    public string UpdatedDisplay
    {
        get
        {
            var diff = DateTime.UtcNow - UpdatedAt.ToUniversalTime();
            if (diff.TotalDays < 1) return "오늘";
            if (diff.TotalDays < 2) return "어제";
            if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}일 전";
            if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}개월 전";
            return $"{(int)(diff.TotalDays / 365)}년 전";
        }
    }
}

public partial class SearchViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly Action<SearchResultItem>? _onOpenDetail;
    private int _page = 1;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _selectedLanguage = "any";
    [ObservableProperty] private string _selectedSort = "stars";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _hasResults;

    public ObservableCollection<SearchResultItem> Results { get; } = new();

    public string[] Languages { get; } =
    {
        "any", "C#", "C++", "C", "CSS", "Dart", "Elixir", "Go", "Haskell",
        "HTML", "Java", "JavaScript", "Kotlin", "Lua", "Objective-C", "PHP",
        "Python", "Ruby", "Rust", "Scala", "Shell", "Swift", "TypeScript", "Vue"
    };

    public (string Key, string Display)[] SortOptions { get; } =
    {
        ("stars", "⭐ Stars"),
        ("updated", "🕒 최근 업데이트"),
        ("forks", "🍴 Forks"),
        ("best-match", "✨ Best match")
    };

    public SearchViewModel(GitHubService github, Action<SearchResultItem>? onOpenDetail = null)
    {
        _github = github;
        _onOpenDetail = onOpenDetail;
    }

    [RelayCommand]
    private void OpenDetail(SearchResultItem? item)
    {
        if (item == null) return;
        _onOpenDetail?.Invoke(item);
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            StatusMessage = "검색어를 입력하세요.";
            return;
        }
        Results.Clear();
        HasResults = false;
        _page = 1;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        _page++;
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        IsBusy = true;
        StatusMessage = "검색 중...";
        try
        {
            var (items, total, _) = await _github.SearchRepositoriesAsync(
                Query.Trim(), SelectedLanguage, SelectedSort, _page);
            foreach (var r in items)
            {
                Results.Add(new SearchResultItem(
                    r.FullName,
                    r.Name,
                    r.Owner?.Login ?? "",
                    r.Owner?.AvatarUrl ?? "",
                    r.Description,
                    r.StargazersCount,
                    r.ForksCount,
                    r.Language,
                    r.UpdatedAt.LocalDateTime,
                    r.HtmlUrl,
                    r.DefaultBranch ?? "main",
                    r.Private
                ));
            }
            TotalCount = total;
            HasMore = Results.Count < total && Results.Count < 1000;
            HasResults = Results.Count > 0;
            StatusMessage = total > 0
                ? $"{total:N0}개 결과 (표시 {Results.Count:N0}개)"
                : "결과가 없습니다.";
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
    private void Clone(SearchResultItem? item)
    {
        if (item == null) return;
        var clone = _github.CreateCloneService();
        if (clone == null) return;
        var repo = new RepoItem(0, item.Name, item.FullName, item.Description,
            item.IsPrivate, item.HtmlUrl, item.DefaultBranch);
        var dlg = new GitUI.Views.Dialogs.CloneDialog(clone, repo, new[] { item.DefaultBranch }, item.DefaultBranch)
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
    }

    [RelayCommand]
    private void OpenInBrowser(SearchResultItem? item)
    {
        if (item == null) return;
        try { Process.Start(new ProcessStartInfo { FileName = item.HtmlUrl, UseShellExecute = true }); }
        catch { }
    }
}
