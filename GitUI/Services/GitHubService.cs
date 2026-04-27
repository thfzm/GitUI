using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Octokit;

namespace GitUI.Services;

public class GitHubService
{
    private GitHubClient? _client;
    private User? _user;
    private string? _token;

    private static readonly HashSet<string> BuiltinExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "__pycache__", ".venv", "venv", "dist", "build", ".next", ".nuxt"
    };

    public bool IsAuthenticated => _client != null && _user != null;
    public string? Username => _user?.Login;
    public string? AvatarUrl => _user?.AvatarUrl;
    public IGitHubClient? Client => _client;

    // ---- Auth ---------------------------------------------------------------

    public async Task<bool> AuthenticateAsync(string token)
    {
        var client = new GitHubClient(new ProductHeaderValue("GitUI"))
        {
            Credentials = new Credentials(token)
        };
        try
        {
            var user = await client.User.Current();
            _client = client;
            _user = user;
            _token = token;
            return true;
        }
        catch
        {
            _client = null;
            _user = null;
            _token = null;
            return false;
        }
    }

    public void Logout()
    {
        _client = null;
        _user = null;
        _token = null;
    }

    // ---- Repositories -------------------------------------------------------

    public async Task<IReadOnlyList<Repository>> GetRepositoriesAsync()
    {
        if (_client == null) return Array.Empty<Repository>();
        return await _client.Repository.GetAllForCurrent(new RepositoryRequest
        {
            Type = RepositoryType.Owner,
            Sort = RepositorySort.Updated,
            Direction = SortDirection.Descending
        });
    }

    public async Task<Repository> CreateRepositoryAsync(NewRepository newRepo)
    {
        Require();
        return await _client!.Repository.Create(newRepo);
    }

    public async Task DeleteRepositoryAsync(string owner, string name)
    {
        Require();
        await _client!.Repository.Delete(owner, name);
    }

    public async Task<Repository> UpdateRepositoryAsync(string owner, string name, RepositoryUpdate update)
    {
        Require();
        return await _client!.Repository.Edit(owner, name, update);
    }

    public Task<Repository> SetVisibilityAsync(string owner, string name, bool isPrivate)
        => UpdateRepositoryAsync(owner, name, new RepositoryUpdate { Name = name, Private = isPrivate });

    public Task<Repository> SetArchivedAsync(string owner, string name, bool archived)
        => UpdateRepositoryAsync(owner, name, new RepositoryUpdate { Name = name, Archived = archived });

    // ---- Branches -----------------------------------------------------------

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync(string owner, string repo)
    {
        Require();
        return await _client!.Repository.Branch.GetAll(owner, repo);
    }

    public async Task<Reference> CreateBranchAsync(string owner, string repo, string newBranch, string fromBranch)
    {
        Require();
        var src = await _client!.Git.Reference.Get(owner, repo, $"heads/{fromBranch}");
        return await _client.Git.Reference.Create(owner, repo, new NewReference($"refs/heads/{newBranch}", src.Object.Sha));
    }

    // ---- Files (Contents API) -----------------------------------------------

    public async Task UploadFileAsync(string owner, string repo, string path, byte[] content, string commitMessage, string branch)
    {
        Require();
        path = path.Replace('\\', '/').TrimStart('/');
        var base64 = Convert.ToBase64String(content);

        try
        {
            var existing = await _client!.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
            var existingFile = existing.FirstOrDefault();
            if (existingFile != null)
            {
                await _client.Repository.Content.UpdateFile(owner, repo, path,
                    new UpdateFileRequest(commitMessage, base64, existingFile.Sha, branch, false));
                return;
            }
        }
        catch (NotFoundException) { }

        await _client!.Repository.Content.CreateFile(owner, repo, path,
            new CreateFileRequest(commitMessage, base64, branch, false));
    }

    public async Task UploadFolderAsync(
        string owner, string repo,
        string folderPath, string commitMessage, string branch,
        string targetPathPrefix = "",
        bool respectGitignore = true,
        IProgress<(int current, int total, string filename)>? progress = null)
    {
        Require();
        var matcher = respectGitignore
            ? GitignoreMatcher.LoadFrom(folderPath)
            : new GitignoreMatcher(folderPath, Array.Empty<string>());

        var files = EnumerateFiles(folderPath, matcher).ToArray();
        for (int i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var rel = Path.GetRelativePath(folderPath, file).Replace('\\', '/');
            var targetPath = string.IsNullOrEmpty(targetPathPrefix) ? rel : $"{targetPathPrefix.TrimEnd('/')}/{rel}";
            progress?.Report((i + 1, files.Length, rel));
            var content = await File.ReadAllBytesAsync(file);
            await UploadFileAsync(owner, repo, targetPath, content, commitMessage, branch);
        }
    }

    public static IEnumerable<string> EnumerateFiles(string root, GitignoreMatcher matcher)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (BuiltinExcludedDirs.Contains(name)) continue;
                if (matcher.IsIgnored(sub, true)) continue;
                stack.Push(sub);
            }
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (matcher.IsIgnored(f, false)) continue;
                yield return f;
            }
        }
    }

    // ---- README -------------------------------------------------------------

    public async Task<(string content, string sha)?> GetReadmeAsync(string owner, string repo, string branch)
    {
        Require();
        try
        {
            var existing = await _client!.Repository.Content.GetAllContentsByRef(owner, repo, "README.md", branch);
            var f = existing.FirstOrDefault();
            if (f == null) return null;
            var bytes = Convert.FromBase64String(f.EncodedContent ?? "");
            return (Encoding.UTF8.GetString(bytes), f.Sha);
        }
        catch (NotFoundException) { return null; }
    }

    public async Task UpdateReadmeAsync(string owner, string repo, string branch, string content, string commitMessage)
    {
        Require();
        var bytes = Encoding.UTF8.GetBytes(content);
        await UploadFileAsync(owner, repo, "README.md", bytes, commitMessage, branch);
    }

    // ---- Commits ------------------------------------------------------------

    public async Task<IReadOnlyList<GitHubCommit>> GetCommitsAsync(string owner, string repo, string branch, int take = 30)
    {
        Require();
        var commits = await _client!.Repository.Commit.GetAll(owner, repo, new CommitRequest { Sha = branch },
            new ApiOptions { PageCount = 1, PageSize = take });
        return commits;
    }

    // ---- Issues / PRs -------------------------------------------------------

    public async Task<IReadOnlyList<Issue>> GetIssuesAsync(string owner, string repo, ItemStateFilter state = ItemStateFilter.Open)
    {
        Require();
        return await _client!.Issue.GetAllForRepository(owner, repo, new RepositoryIssueRequest { State = state });
    }

    public async Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string owner, string repo, ItemStateFilter state = ItemStateFilter.Open)
    {
        Require();
        var prState = state switch
        {
            ItemStateFilter.Open => ItemStateFilter.Open,
            ItemStateFilter.Closed => ItemStateFilter.Closed,
            _ => ItemStateFilter.All
        };
        return await _client!.PullRequest.GetAllForRepository(owner, repo, new PullRequestRequest { State = prState });
    }

    // ---- Releases -----------------------------------------------------------

    public async Task<Release> CreateReleaseAsync(string owner, string repo, string tag, string? name, string? body, bool prerelease, string targetBranch)
    {
        Require();
        return await _client!.Repository.Release.Create(owner, repo, new NewRelease(tag)
        {
            Name = name,
            Body = body,
            Prerelease = prerelease,
            TargetCommitish = targetBranch
        });
    }

    public async Task<IReadOnlyList<Release>> GetReleasesAsync(string owner, string repo)
    {
        Require();
        return await _client!.Repository.Release.GetAll(owner, repo);
    }

    // ---- GitHub Pages -------------------------------------------------------

    public async Task<(bool enabled, string? url)> GetPagesStatusAsync(string owner, string repo)
    {
        Require();
        try
        {
            var info = await _client!.Repository.Page.Get(owner, repo);
            return (true, info?.HtmlUrl);
        }
        catch (NotFoundException) { return (false, null); }
        catch { return (false, null); }
    }

    /// <summary>
    /// Enable Pages on a repo by hitting the REST endpoint directly (Octokit's typed API for this is limited).
    /// </summary>
    public async Task EnablePagesAsync(string owner, string repo, string branch)
    {
        if (_token == null) throw new InvalidOperationException("Not authenticated.");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GitUI");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        var body = JsonSerializer.Serialize(new
        {
            source = new { branch, path = "/" }
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await http.PostAsync($"https://api.github.com/repos/{owner}/{repo}/pages", content);
        if (!resp.IsSuccessStatusCode)
        {
            var msg = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Pages 활성화 실패: {(int)resp.StatusCode} {msg}");
        }
    }

    public async Task DisablePagesAsync(string owner, string repo)
    {
        if (_token == null) throw new InvalidOperationException("Not authenticated.");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GitUI");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        var resp = await http.DeleteAsync($"https://api.github.com/repos/{owner}/{repo}/pages");
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var msg = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Pages 비활성화 실패: {(int)resp.StatusCode} {msg}");
        }
    }

    // ---- Clipboard image upload --------------------------------------------

    public Task UploadImageAsync(string owner, string repo, byte[] pngBytes, string commitMessage, string branch, string folder = "screenshots")
    {
        var name = $"{DateTime.Now:yyyyMMdd-HHmmss}.png";
        var path = $"{folder.TrimEnd('/')}/{name}";
        return UploadFileAsync(owner, repo, path, pngBytes, commitMessage, branch);
    }

    // ---- Internal -----------------------------------------------------------

    private void Require()
    {
        if (_client == null) throw new InvalidOperationException("Not authenticated.");
    }
}
