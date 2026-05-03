using System;
using System.IO;
using System.Windows;
using GitUI.Services;
using Octokit;

namespace GitUI.Views.Dialogs;

public partial class CreateRepoFromFolderDialog : Window
{
    private readonly GitHubService _github;
    private readonly string _folderPath;
    public Repository? Created { get; private set; }

    public CreateRepoFromFolderDialog(GitHubService github, string folderPath)
    {
        _github = github;
        _folderPath = folderPath;
        InitializeComponent();
        FolderPathLabel.Text = folderPath;
        NameBox.Text = SanitizeRepoName(Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusLabel.Text = "이름을 입력하세요.";
            return;
        }

        SetBusy(true, "리포지토리 생성 중...");
        try
        {
            var newRepo = new NewRepository(name)
            {
                Description = string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text,
                Private = PrivateCheck.IsChecked == true,
                AutoInit = true
            };
            var repo = await _github.CreateRepositoryAsync(newRepo);

            // Wait briefly for the default branch to exist (AutoInit creates README + initial commit)
            await System.Threading.Tasks.Task.Delay(800);

            var owner = repo.FullName.Split('/')[0];
            var branch = repo.DefaultBranch ?? "main";
            var matcher = GitignoreCheck.IsChecked == true
                ? GitignoreMatcher.LoadFrom(_folderPath)
                : new GitignoreMatcher(_folderPath, Array.Empty<string>());

            var files = System.Linq.Enumerable.ToArray(GitHubService.EnumerateFiles(_folderPath, matcher));
            var upserts = new System.Collections.Generic.List<(string path, byte[] content)>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                var f = files[i];
                var rel = Path.GetRelativePath(_folderPath, f).Replace('\\', '/');
                StatusLabel.Text = $"읽는 중 {i + 1}/{files.Length} · {rel}";
                var content = await File.ReadAllBytesAsync(f);
                upserts.Add((rel, content));
            }
            StatusLabel.Text = $"GitHub에 {upserts.Count}개 파일을 1커밋으로 푸시 중...";
            var prog = new Progress<(int current, int total, string filename)>(t =>
            {
                StatusLabel.Text = $"{t.current}/{t.total} · {t.filename}";
                Progress.Value = (double)t.current / Math.Max(1, t.total) * 100;
            });
            await _github.BulkCommitAsync(owner, repo.Name, branch,
                "Initial upload via GitUI", upserts, Array.Empty<string>(), prog,
                notify: m => Dispatcher.Invoke(() => StatusLabel.Text = m));

            Created = repo;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "오류: " + ex.Message;
            SetBusy(false, StatusLabel.Text);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        CreateButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        NameBox.IsEnabled = !busy;
        DescBox.IsEnabled = !busy;
        PrivateCheck.IsEnabled = !busy;
        GitignoreCheck.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Text = status;
    }

    private static string SanitizeRepoName(string name)
    {
        var s = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') s.Append(c);
            else if (char.IsWhiteSpace(c)) s.Append('-');
        }
        return s.ToString().Trim('-', '.', '_');
    }
}
