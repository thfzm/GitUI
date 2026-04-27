using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;

namespace GitUI.ViewModels.Tabs;

public partial class ReadmeTabViewModel : ObservableObject
{
    private readonly GitHubService _github;
    public RepoItem Repo { get; }
    public Func<string> CurrentBranchAccessor { get; }
    private string? _existingSha;

    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _loaded;

    public ReadmeTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch)
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
            var result = await _github.GetReadmeAsync(owner, Repo.Name, CurrentBranchAccessor());
            if (result.HasValue)
            {
                Content = result.Value.content;
                _existingSha = result.Value.sha;
            }
            else
            {
                Content = $"# {Repo.Name}\n\n";
                _existingSha = null;
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
    private async Task SaveAsync()
    {
        IsBusy = true;
        StatusMessage = "저장 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            await _github.UpdateReadmeAsync(owner, Repo.Name, CurrentBranchAccessor(), Content, "Update README via GitUI");
            StatusMessage = "저장됨.";
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
