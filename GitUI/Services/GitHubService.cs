using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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

    // ---- Star / Fork --------------------------------------------------------

    public async Task<bool> IsStarredAsync(string owner, string repo)
    {
        Require();
        try { return await _client!.Activity.Starring.CheckStarred(owner, repo); }
        catch { return false; }
    }

    public async Task StarAsync(string owner, string repo)
    {
        Require();
        await _client!.Activity.Starring.StarRepo(owner, repo);
    }

    public async Task UnstarAsync(string owner, string repo)
    {
        Require();
        await _client!.Activity.Starring.RemoveStarFromRepo(owner, repo);
    }

    public async Task<Repository> ForkAsync(string owner, string repo)
    {
        Require();
        return await _client!.Repository.Forks.Create(owner, repo, new NewRepositoryFork());
    }

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
        var upserts = new List<(string path, byte[] content)>(files.Length);
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(folderPath, file).Replace('\\', '/');
            var targetPath = string.IsNullOrEmpty(targetPathPrefix) ? rel : $"{targetPathPrefix.TrimEnd('/')}/{rel}";
            var content = await File.ReadAllBytesAsync(file);
            upserts.Add((targetPath, content));
        }
        await BulkCommitAsync(owner, repo, branch, commitMessage, upserts, Array.Empty<string>(), progress);
    }

    /// <summary>
    /// Commits many file additions/updates and deletions in a SINGLE commit via the Git Data API.
    /// Blob uploads run in parallel (capped to avoid GitHub secondary rate limits).
    /// On secondary-rate-limit / abuse responses we honor Retry-After and resume automatically.
    /// Total API cost: ~N+4 calls regardless of file count, vs 2N with the Contents API.
    /// History: 1 commit instead of N.
    /// </summary>
    public async Task BulkCommitAsync(
        string owner, string repo, string branch, string commitMessage,
        IReadOnlyList<(string path, byte[] content)> upserts,
        IReadOnlyList<string> deletes,
        IProgress<(int current, int total, string filename)>? progress = null,
        Action<string>? notify = null)
    {
        Require();
        if (upserts.Count == 0 && deletes.Count == 0) return;
        if (_token == null) throw new InvalidOperationException("Not authenticated.");
        var client = _client!;

        // 1. Get current branch tip
        var reference = await RetryOnRateLimitAsync(
            () => client.Git.Reference.Get(owner, repo, $"heads/{branch}"), notify);
        var latestCommitSha = reference.Object.Sha;
        var latestCommit = await RetryOnRateLimitAsync(
            () => client.Git.Commit.Get(owner, repo, latestCommitSha), notify);

        // 2. Create blobs in parallel. Concurrency is intentionally low (3) because GitHub's
        // secondary rate limit triggers on bursts of content-creation calls. If we still hit it,
        // we honor Retry-After per-blob and resume.
        var blobShas = new string[upserts.Count];
        int done = 0;
        using (var sem = new SemaphoreSlim(3))
        {
            var tasks = new Task[upserts.Count];
            for (int i = 0; i < upserts.Count; i++)
            {
                int idx = i;
                tasks[i] = Task.Run(async () =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        var (path, content) = upserts[idx];
                        var blob = await RetryOnRateLimitAsync(
                            () => client.Git.Blob.Create(owner, repo, new NewBlob
                            {
                                Content = Convert.ToBase64String(content),
                                Encoding = EncodingType.Base64
                            }),
                            notify);
                        blobShas[idx] = blob.Sha;
                        int n = Interlocked.Increment(ref done);
                        progress?.Report((n, upserts.Count, path));
                    }
                    finally { sem.Release(); }
                });
            }
            await Task.WhenAll(tasks);
        }

        // 3. Build a tree with all blobs and explicit deletes (sha:null).
        // Octokit doesn't reliably serialize Sha=null on NewTreeItem, so we POST raw JSON.
        // Large trees are chunked: GitHub returns 422 "request timed out" if a single create has too many entries.
        // Each chunk's resulting tree becomes the base_tree of the next.
        var allEntries = new List<TreeEntry>(upserts.Count + deletes.Count);
        for (int i = 0; i < upserts.Count; i++)
            allEntries.Add(new TreeEntry(upserts[i].path.Replace('\\', '/').TrimStart('/'), blobShas[i]));
        foreach (var del in deletes)
            allEntries.Add(new TreeEntry(del.Replace('\\', '/').TrimStart('/'), null));

        const int treeChunkSize = 200;
        int chunkCount = (allEntries.Count + treeChunkSize - 1) / treeChunkSize;
        if (chunkCount == 0) chunkCount = 1;
        string currentTreeSha = latestCommit.Tree.Sha;

        for (int c = 0; c < chunkCount; c++)
        {
            var slice = allEntries.GetRange(
                c * treeChunkSize,
                Math.Min(treeChunkSize, allEntries.Count - c * treeChunkSize));
            string baseTree = currentTreeSha;
            int chunkIndex = c;
            if (chunkCount > 1)
                notify?.Invoke($"Tree 청크 {chunkIndex + 1}/{chunkCount} 생성 중 ({slice.Count} entries)...");

            currentTreeSha = await RetryOnRateLimitAsync(async () =>
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("GitUI");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

                var json = BuildNewTreeJson(baseTree, slice);
                using var body = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await http.PostAsync($"https://api.github.com/repos/{owner}/{repo}/git/trees", body);
                var respText = await resp.Content.ReadAsStringAsync();
                int code = (int)resp.StatusCode;
                if (code == 403 && IsSecondaryRateLimitMessage(respText))
                {
                    int wait = ParseRetryAfter(resp) ?? 60;
                    throw new SecondaryLimitMarker(wait);
                }
                // 5xx is transient (GitHub backend timeout). Retry with backoff.
                if (code >= 500) throw new TransientServerMarker(code);
                // 422 with "timed out" / "too large" — also transient on retry; if persistent, the chunk size is too big.
                if (code == 422 && (respText.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                                    || respText.Contains("too large", StringComparison.OrdinalIgnoreCase)))
                    throw new TransientServerMarker(code);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Tree 생성 실패: {code} {respText}");
                using var treeDoc = JsonDocument.Parse(respText);
                return treeDoc.RootElement.GetProperty("sha").GetString()!;
            }, notify);
        }
        var newTreeSha = currentTreeSha;

        // 4. Create commit + advance ref
        var commit = await RetryOnRateLimitAsync(
            () => client.Git.Commit.Create(owner, repo,
                new NewCommit(commitMessage, newTreeSha, latestCommitSha)),
            notify);
        await RetryOnRateLimitAsync(
            () => client.Git.Reference.Update(owner, repo, $"heads/{branch}",
                new ReferenceUpdate(commit.Sha)),
            notify);
    }

    private sealed class SecondaryLimitMarker : Exception
    {
        public int RetryAfterSeconds { get; }
        public SecondaryLimitMarker(int seconds) : base($"secondary rate limit (retry after {seconds}s)")
        {
            RetryAfterSeconds = seconds;
        }
    }

    private sealed class TransientServerMarker : Exception
    {
        public int StatusCode { get; }
        public TransientServerMarker(int statusCode) : base($"transient server error {statusCode}")
        {
            StatusCode = statusCode;
        }
    }

    private static bool IsSecondaryRateLimitMessage(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        return body.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase)
            || body.Contains("abuse detection", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseRetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("Retry-After", out var vals))
        {
            foreach (var v in vals)
                if (int.TryParse(v, out var s)) return s;
        }
        return null;
    }

    private static async Task<T> RetryOnRateLimitAsync<T>(Func<Task<T>> op, Action<string>? notify)
    {
        const int maxAttempts = 6;
        for (int attempt = 1; ; attempt++)
        {
            try { return await op(); }
            catch (Exception ex) when (attempt < maxAttempts && TryGetRetryWait(ex, attempt, out var wait, out var label))
            {
                notify?.Invoke($"{label} — {wait.TotalSeconds:0}초 대기 후 재시도 ({attempt}/{maxAttempts - 1})");
                await Task.Delay(wait);
            }
        }
    }

    private static Task RetryOnRateLimitAsync(Func<Task> op, Action<string>? notify)
        => RetryOnRateLimitAsync<bool>(async () => { await op(); return true; }, notify);

    private static bool TryGetRetryWait(Exception ex, int attempt, out TimeSpan wait, out string label)
    {
        // Octokit's secondary-limit/abuse exception
        if (ex is AbuseException ab)
        {
            wait = TimeSpan.FromSeconds(ab.RetryAfterSeconds ?? 60);
            label = "GitHub 레이트리밋";
            return true;
        }
        // Primary rate limit
        if (ex is RateLimitExceededException rl)
        {
            wait = rl.Reset - DateTimeOffset.UtcNow;
            if (wait < TimeSpan.FromSeconds(5)) wait = TimeSpan.FromSeconds(60);
            label = "GitHub 1차 레이트리밋";
            return true;
        }
        // Our own raw-HTTP secondary-limit marker
        if (ex is SecondaryLimitMarker sm)
        {
            wait = TimeSpan.FromSeconds(sm.RetryAfterSeconds);
            label = "GitHub 레이트리밋";
            return true;
        }
        // 5xx server errors (502/503/504) — transient. Exponential backoff.
        if (ex is TransientServerMarker tx)
        {
            wait = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, attempt - 1)));  // 5,10,20,40,60,60
            label = $"GitHub 일시 오류 {tx.StatusCode}";
            return true;
        }
        if (ex is ApiException apiSvr)
        {
            int code = (int)apiSvr.StatusCode;
            if (code >= 500 && code <= 599)
            {
                wait = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, attempt - 1)));
                label = $"GitHub 일시 오류 {code}";
                return true;
            }
        }
        // Network blips
        if (ex is HttpRequestException || ex is TaskCanceledException)
        {
            wait = TimeSpan.FromSeconds(Math.Min(30, 3 * Math.Pow(2, attempt - 1)));
            label = "네트워크 일시 오류";
            return true;
        }
        // Fallback: 403 + secondary-limit message that slipped through Octokit typing
        var msg = ex.Message ?? "";
        if (ex is ApiException api && (int)api.StatusCode == 403 && IsSecondaryRateLimitMessage(msg))
        {
            wait = TimeSpan.FromSeconds(60);
            label = "GitHub 레이트리밋";
            return true;
        }
        wait = default;
        label = "";
        return false;
    }

    /// <summary>Path + (blobSha or null = delete).</summary>
    private record TreeEntry(string Path, string? BlobSha);

    private static string BuildNewTreeJson(string baseTreeSha, IReadOnlyList<TreeEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"base_tree\":");
        sb.Append(JsonSerializer.Serialize(baseTreeSha));
        sb.Append(",\"tree\":[");
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var e = entries[i];
            sb.Append('{');
            sb.Append("\"path\":");
            sb.Append(JsonSerializer.Serialize(e.Path));
            sb.Append(",\"mode\":\"100644\",\"type\":\"blob\",\"sha\":");
            sb.Append(e.BlobSha == null ? "null" : JsonSerializer.Serialize(e.BlobSha));
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
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
