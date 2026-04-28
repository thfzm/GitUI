using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Services;
using Octokit;

namespace GitUI.ViewModels;

public record LicenseOption(string Key, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public partial class CreateRepoViewModel : ObservableObject
{
    private readonly GitHubService _github;
    private readonly Func<Repository, Task> _onCreated;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isPrivate;
    [ObservableProperty] private bool _autoInit = true;
    [ObservableProperty] private string? _gitignoreTemplate;
    [ObservableProperty] private LicenseOption? _selectedLicense;
    [ObservableProperty] private ProjectTemplate _selectedProjectTemplate = ProjectTemplates.None;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _progressLabel;
    [ObservableProperty] private bool _templatesLoaded;

    public ObservableCollection<string> GitignoreTemplates { get; } = new() { "" };
    public ObservableCollection<LicenseOption> Licenses { get; } = new()
    {
        new LicenseOption("", "(없음)")
    };
    public IReadOnlyList<ProjectTemplate> ProjectTemplateOptions { get; } = ProjectTemplates.All;

    public CreateRepoViewModel(GitHubService github, Func<Repository, Task> onCreated)
    {
        _github = github;
        _onCreated = onCreated;
        _ = LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync()
    {
        try
        {
            var ignoreList = await _github.GetGitignoreTemplatesAsync();
            GitignoreTemplates.Clear();
            GitignoreTemplates.Add("");
            foreach (var t in ignoreList) GitignoreTemplates.Add(t);

            var licenseList = await _github.GetLicenseTemplatesAsync();
            Licenses.Clear();
            Licenses.Add(new LicenseOption("", "(없음)"));
            foreach (var (key, name) in licenseList)
                Licenses.Add(new LicenseOption(key, name));

            TemplatesLoaded = true;
        }
        catch { }
    }

    /// <summary>
    /// When user picks a project template, optionally suggest matching .gitignore.
    /// </summary>
    partial void OnSelectedProjectTemplateChanged(ProjectTemplate value)
    {
        if (value?.GitignoreSuggestion is { } sug && string.IsNullOrEmpty(GitignoreTemplate))
        {
            // Match case-insensitively against loaded list
            var match = GitignoreTemplates.FirstOrDefault(t => string.Equals(t, sug, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match)) GitignoreTemplate = match;
        }
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
                LicenseTemplate = string.IsNullOrEmpty(SelectedLicense?.Key) ? null : SelectedLicense.Key
            };
            var result = await _github.CreateRepositoryAsync(newRepo);
            StatusMessage = $"'{result.FullName}' 생성됨.";

            if (SelectedProjectTemplate.Files.Count > 0)
            {
                StatusMessage = $"스타터 파일 업로드 중 ({SelectedProjectTemplate.DisplayName})...";
                // Wait briefly for AutoInit to settle
                if (AutoInit) await Task.Delay(800);
                await UploadStarterFilesAsync(result);
            }

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
            Progress = 0;
            ProgressLabel = null;
        }
    }

    private async Task UploadStarterFilesAsync(Repository repo)
    {
        var owner = repo.Owner?.Login ?? repo.FullName.Split('/')[0];
        var branch = repo.DefaultBranch ?? "main";
        var files = SelectedProjectTemplate.Files;
        var commitMsg = $"Add {SelectedProjectTemplate.DisplayName} starter files";

        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var path = ProjectTemplates.ApplyPlaceholders(f.Path, repo.Name, owner);
            var content = ProjectTemplates.ApplyPlaceholders(f.Content, repo.Name, owner);
            ProgressLabel = $"{i + 1}/{files.Count} · {path}";
            Progress = (double)(i + 1) / files.Count * 100;
            await _github.UploadFileAsync(owner, repo.Name, path, Encoding.UTF8.GetBytes(content), commitMsg, branch);
        }
    }

    private void Reset()
    {
        Name = "";
        Description = "";
        IsPrivate = false;
        AutoInit = true;
        GitignoreTemplate = "";
        SelectedLicense = Licenses.FirstOrDefault();
        SelectedProjectTemplate = ProjectTemplates.None;
    }
}
