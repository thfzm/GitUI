using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.ViewModels.Tabs;

public record CommitItem(string Sha, string ShortSha, string Message, string Author, DateTime Date, string HtmlUrl);

public partial class HistoryTabViewModel : ObservableObject
{
    private readonly GitHubService _github;
    public RepoItem Repo { get; }
    public Func<string> CurrentBranchAccessor { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _loaded;

    public ObservableCollection<CommitItem> Commits { get; } = new();

    public HistoryTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch)
    {
        _github = github;
        Repo = repo;
        CurrentBranchAccessor = currentBranch;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = "불러오는 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var commits = await _github.GetCommitsAsync(owner, Repo.Name, CurrentBranchAccessor(), 30);
            Commits.Clear();
            foreach (var c in commits)
            {
                Commits.Add(new CommitItem(
                    c.Sha,
                    c.Sha[..7],
                    c.Commit.Message.Split('\n')[0],
                    c.Commit.Author?.Name ?? "(unknown)",
                    c.Commit.Author?.Date.LocalDateTime ?? DateTime.MinValue,
                    c.HtmlUrl
                ));
            }
            Loaded = true;
            StatusMessage = null;
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
    private void OpenCommit(CommitItem item)
    {
        if (item == null) return;
        try { Process.Start(new ProcessStartInfo { FileName = item.HtmlUrl, UseShellExecute = true }); }
        catch { }
    }
}
