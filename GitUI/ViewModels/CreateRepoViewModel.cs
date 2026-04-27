using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Services;
using Octokit;

namespace GitUI.ViewModels;

public partial class CreateRepoViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly Func<Repository, Task> _onCreated;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isPrivate;
    [ObservableProperty] private bool _autoInit = true;
    [ObservableProperty] private string _gitignoreTemplate = "";
    [ObservableProperty] private string _licenseTemplate = "";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;

    public string[] GitignoreTemplates { get; } =
        { "", "VisualStudio", "Node", "Python", "Unity", "Java", "Go", "Rust", "C++" };

    public string[] LicenseTemplates { get; } =
        { "", "mit", "apache-2.0", "gpl-3.0", "bsd-3-clause", "unlicense" };

    public CreateRepoViewModel(GitHubService github, Func<Repository, Task> onCreated)
    {
        _github = github;
        _onCreated = onCreated;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "리포지토리 이름을 입력하세요.";
            return;
        }
        IsBusy = true;
        StatusMessage = "생성 중...";
        try
        {
            var newRepo = new NewRepository(Name.Trim())
            {
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
                Private = IsPrivate,
                AutoInit = AutoInit,
                GitignoreTemplate = string.IsNullOrEmpty(GitignoreTemplate) ? null : GitignoreTemplate,
                LicenseTemplate = string.IsNullOrEmpty(LicenseTemplate) ? null : LicenseTemplate
            };
            var result = await _github.CreateRepositoryAsync(newRepo);
            StatusMessage = $"'{result.FullName}' 생성됨.";
            await _onCreated(result);
            Reset();
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

    private void Reset()
    {
        Name = "";
        Description = "";
        IsPrivate = false;
        AutoInit = true;
        GitignoreTemplate = "";
        LicenseTemplate = "";
    }
}
