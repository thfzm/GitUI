using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;
using Octokit;

namespace GitUI.ViewModels.Tabs;

public record IssueOrPrItem(int Number, string Title, string Author, DateTime CreatedAt, string Url, bool IsPullRequest, string State);

public partial class IssuesTabViewModel : ObservableObject
{
    private readonly GitHubService _github;
    public RepoItem Repo { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _showOpen = true;
    [ObservableProperty] private bool _loaded;

    public ObservableCollection<IssueOrPrItem> Items { get; } = new();

    public IssuesTabViewModel(GitHubService github, RepoItem repo)
    {
        _github = github;
        Repo = repo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = "불러오는 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var state = ShowOpen ? ItemStateFilter.Open : ItemStateFilter.Closed;
            var issues = await _github.GetIssuesAsync(owner, Repo.Name, state);
            Items.Clear();
            foreach (var i in issues)
            {
                Items.Add(new IssueOrPrItem(
                    i.Number, i.Title, i.User?.Login ?? "(unknown)",
                    i.CreatedAt.LocalDateTime, i.HtmlUrl,
                    i.PullRequest != null, i.State.StringValue ?? "open"));
            }
            Loaded = true;
            StatusMessage = $"{Items.Count}건";
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
    private async Task ToggleStateAsync()
    {
        ShowOpen = !ShowOpen;
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenItem(IssueOrPrItem item)
    {
        if (item == null) return;
        try { Process.Start(new ProcessStartInfo { FileName = item.Url, UseShellExecute = true }); }
        catch { }
    }
}
