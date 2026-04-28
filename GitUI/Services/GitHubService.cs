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

    public CloneService? CreateCloneService()
    {
        if (_token == null || _user == null) return null;
        return new CloneService(_token, _user.Login);
    }

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

    /// <summary>
    /// Searches public repositories on GitHub using the Search API.
    /// </summary>
    public async Task<(IReadOnlyList<Repository> items, int totalCount, bool incomplete)> SearchRepositoriesAsync(
        string query, string? language, string sort, int page, int perPage = 25)
    {
        Require();
        var fullQuery = string.IsNullOrEmpty(language) || language == "any"
            ? query
            : $"{query} language:{language}";

        var req = new SearchRepositoriesRequest(fullQuery)
        {
            PerPage = perPage,
            Page = page,
            Order = SortDirection.Descending
        };
        req.SortField = sort switch
        {
            "stars" => RepoSearchSort.Stars,
            "forks" => RepoSearchSort.Forks,
            "updated" => RepoSearchSort.Updated,
            _ => null
        };

        var result = await _client!.Search.SearchRepo(req);
        return (result.Items, result.TotalCount, result.IncompleteResults);
    }

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

    /// <summary>
    /// Fetches the full list of supported .gitignore templates from GitHub
    /// (~80 templates including most popular languages and frameworks).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetGitignoreTemplatesAsync()
    {
        Require();
        try
        {
            return await _client!.GitIgnore.GetAllGitIgnoreTemplates();
        }
        catch
        {
            return new[] { "C", "C++", "Go", "Java", "Node", "Python", "Rust", "Unity", "VisualStudio" };
        }
    }

    /// <summary>
    /// Fetches the list of available license keys.
    /// </summary>
    public async Task<IReadOnlyList<(string key, string name)>> GetLicenseTemplatesAsync()
    {
        Require();
        try
        {
            var licenses = await _client!.Licenses.GetAllLicenses();
            return licenses.Select(l => (l.Key, l.Name)).ToArray();
        }
        catch
        {
            return new[]
            {
                ("mit", "MIT"),
                ("apache-2.0", "Apache 2.0"),
                ("gpl-3.0", "GPL 3.0"),
                ("bsd-3-clause", "BSD 3-Clause"),
                ("unlicense", "Unlicense")
            };
        }
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

        // Octokit's Contents API rejects empty content (CreateFileRequest validates non-empty).
        // Use the Git Data API path to preserve genuinely empty files like __init__.py / .gitkeep.
        if (content.Length == 0)
        {
            await UploadEmptyFileViaGitDataAsync(owner, repo, path, commitMessage, branch);
            return;
        }

        var base64 = Convert.ToBase64String(content);

        // Retry on SHA-mismatch conflicts (409/422). These happen when the file was
        // updated between our GetAllContentsByRef and our UpdateFile call —
        // e.g. concurrent sync, multiple watchers, or GitHub-side replication delay.
        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
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
                catch (NotFoundException) { /* file does not exist, fall through to create */ }

                await _client!.Repository.Content.CreateFile(owner, repo, path,
                    new CreateFileRequest(commitMessage, base64, branch, false));
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsShaConflict(ex))
            {
                // Stale SHA — wait briefly, then refetch and retry. Backoff up to ~1.5s.
                await Task.Delay(200 * attempt);
            }
        }
    }

    private static bool IsShaConflict(Exception ex)
    {
        // Octokit can surface SHA mismatches as ApiValidationException (422) or ApiException (409),
        // and the message format is typically "... is at <sha> but expected <sha>".
        if (ex is ApiException api)
        {
            var code = (int)api.StatusCode;
            if (code == 409 || code == 422) return true;
        }
        var msg = ex.Message ?? "";
        return msg.Contains("is at", StringComparison.OrdinalIgnoreCase)
            && msg.Contains("expected", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deletes a file via the Contents API. Retries on stale-SHA conflicts by refetching.
    /// Treats 404 as success (file already gone).
    /// </summary>
    public async Task DeleteFileAsync(string owner, string repo, string path, string? sha, string commitMessage, string branch)
    {
        Require();
        path = path.Replace('\\', '/').TrimStart('/');

        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            string? currentSha = sha;
            // After the first attempt (or if no sha was provided), refetch to get fresh SHA.
            if (attempt > 1 || string.IsNullOrEmpty(currentSha))
            {
                try
                {
                    var existing = await _client!.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
                    var existingFile = existing.FirstOrDefault();
                    if (existingFile == null) return;  // already gone
                    currentSha = existingFile.Sha;
                }
                catch (NotFoundException) { return; }
            }

            try
            {
                await _client!.Repository.Content.DeleteFile(owner, repo, path,
                    new DeleteFileRequest(commitMessage, currentSha!, branch));
                return;
            }
            catch (NotFoundException) { return; }
            catch (Exception ex) when (attempt < maxAttempts && IsShaConflict(ex))
            {
                await Task.Delay(200 * attempt);
            }
        }
    }

    private async Task UploadEmptyFileViaGitDataAsync(string owner, string repo, string path, string commitMessage, string branch)
    {
        var client = _client!;

        // 1. Create an empty blob.
        var blob = await client.Git.Blob.Create(owner, repo, new NewBlob
        {
            Content = "",
            Encoding = EncodingType.Utf8
        });

        // 2. Get the latest commit on the target branch.
        var reference = await client.Git.Reference.Get(owner, repo, $"heads/{branch}");
        var latestCommit = await client.Git.Commit.Get(owner, repo, reference.Object.Sha);

        // 3. Build a new tree based on the latest, with the empty blob at `path`.
        var newTree = new NewTree { BaseTree = latestCommit.Tree.Sha };
        newTree.Tree.Add(new NewTreeItem
        {
            Path = path,
            Mode = "100644",
            Type = TreeType.Blob,
            Sha = blob.Sha
        });
        var tree = await client.Git.Tree.Create(owner, repo, newTree);

        // 4. Create the commit and advance the branch ref.
        var newCommit = new NewCommit(commitMessage, tree.Sha, latestCommit.Sha);
        var commit = await client.Git.Commit.Create(owner, repo, newCommit);
        await client.Git.Reference.Update(owner, repo, $"heads/{branch}", new ReferenceUpdate(commit.Sha));
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

    /// <summary>
    /// Auto-generates release notes via GitHub API based on commits/PRs since the previous tag.
    /// </summary>
    public async Task<(string name, string body)> GenerateReleaseNotesAsync(
        string owner, string repo, string tag, string? previousTag, string targetBranch)
    {
        Require();
        var req = new GenerateReleaseNotesRequest(tag)
        {
            TargetCommitish = targetBranch,
            PreviousTagName = previousTag
        };
        var result = await _client!.Repository.Release.GenerateReleaseNotes(owner, repo, req);
        return (result.Name, result.Body);
    }

    public async Task<string?> GetLatestReleaseTagAsync(string owner, string repo)
    {
        Require();
        try
        {
            var releases = await _client!.Repository.Release.GetAll(owner, repo,
                new ApiOptions { PageCount = 1, PageSize = 5 });
            return releases.FirstOrDefault()?.TagName;
        }
        catch { return null; }
    }

    public async Task<ReleaseAsset> UploadReleaseAssetAsync(
        Release release, string fileName, byte[] data, string? contentType = null)
    {
        Require();
        using var stream = new MemoryStream(data);
        var upload = new ReleaseAssetUpload
        {
            FileName = fileName,
            ContentType = contentType ?? "application/octet-stream",
            RawData = stream
        };
        return await _client!.Repository.Release.UploadAsset(release, upload);
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

    // ---- File browsing & download ------------------------------------------

    public async Task<TreeResponse> GetRepoTreeAsync(string owner, string repo, string branch)
    {
        Require();
        var reference = await _client!.Git.Reference.Get(owner, repo, $"heads/{branch}");
        var commit = await _client.Git.Commit.Get(owner, repo, reference.Object.Sha);
        return await _client.Git.Tree.GetRecursive(owner, repo, commit.Tree.Sha);
    }

    /// <summary>
    /// Reads a single file's contents. Falls back to the Git Blob API when the file is too
    /// large for the Contents API to inline (>1MB).
    /// </summary>
    public async Task<byte[]> GetFileBytesAsync(string owner, string repo, string path, string branch)
    {
        Require();
        var contents = await _client!.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
        var f = contents.FirstOrDefault();
        if (f == null) throw new FileNotFoundException(path);
        if (!string.IsNullOrEmpty(f.EncodedContent))
            return Convert.FromBase64String(f.EncodedContent);
        var blob = await _client.Git.Blob.Get(owner, repo, f.Sha);
        return Convert.FromBase64String(blob.Content);
    }

    public async Task<byte[]> GetBlobBytesAsync(string owner, string repo, string sha)
    {
        Require();
        var blob = await _client!.Git.Blob.Get(owner, repo, sha);
        return Convert.FromBase64String(blob.Content);
    }

    public async Task<byte[]> DownloadRepoArchiveAsync(string owner, string repo, string branch)
    {
        Require();
        return await _client!.Repository.Content.GetArchive(owner, repo, ArchiveFormat.Zipball, branch);
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
