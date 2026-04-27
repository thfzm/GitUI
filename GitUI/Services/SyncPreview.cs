using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Octokit;

namespace GitUI.Services;

public enum FileChange { Added, Modified, Unchanged, Skipped }

public record SyncFileEntry(string LocalPath, string TargetPath, long Size, FileChange Change, string? Reason);

public record SyncPreviewResult(
    IReadOnlyList<SyncFileEntry> Entries,
    int Added,
    int Modified,
    int Unchanged,
    int Skipped);

public static class SyncPreview
{
    /// <summary>
    /// Walks <paramref name="folderPath"/>, applies .gitignore + built-in excludes,
    /// and compares each file against the remote tree to classify it.
    /// </summary>
    public static async Task<SyncPreviewResult> ComputeAsync(
        IGitHubClient client,
        string owner, string repo, string branch,
        string folderPath, string targetPrefix)
    {
        var matcher = GitignoreMatcher.LoadFrom(folderPath);
        var remote = await BuildRemoteShaMapAsync(client, owner, repo, branch);

        var entries = new List<SyncFileEntry>();
        int added = 0, modified = 0, unchanged = 0, skipped = 0;

        foreach (var file in EnumerateFiles(folderPath, matcher))
        {
            var rel = Path.GetRelativePath(folderPath, file).Replace('\\', '/');
            var target = string.IsNullOrEmpty(targetPrefix) ? rel : $"{targetPrefix.TrimEnd('/')}/{rel}";
            var info = new FileInfo(file);

            if (info.Length > 100 * 1024 * 1024)
            {
                entries.Add(new SyncFileEntry(file, target, info.Length, FileChange.Skipped, "100MB 초과 (Contents API 불가)"));
                skipped++;
                continue;
            }

            var localSha = await ComputeGitBlobShaAsync(file);
            if (remote.TryGetValue(target, out var remoteSha))
            {
                if (string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(new SyncFileEntry(file, target, info.Length, FileChange.Unchanged, null));
                    unchanged++;
                }
                else
                {
                    entries.Add(new SyncFileEntry(file, target, info.Length, FileChange.Modified, null));
                    modified++;
                }
            }
            else
            {
                entries.Add(new SyncFileEntry(file, target, info.Length, FileChange.Added, null));
                added++;
            }
        }

        return new SyncPreviewResult(entries, added, modified, unchanged, skipped);
    }

    private static async Task<Dictionary<string, string>> BuildRemoteShaMapAsync(
        IGitHubClient client, string owner, string repo, string branch)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var reference = await client.Git.Reference.Get(owner, repo, $"heads/{branch}");
            var commit = await client.Git.Commit.Get(owner, repo, reference.Object.Sha);
            var tree = await client.Git.Tree.GetRecursive(owner, repo, commit.Tree.Sha);
            foreach (var item in tree.Tree)
            {
                if (item.Type.Value == TreeType.Blob)
                    map[item.Path] = item.Sha;
            }
        }
        catch
        {
            // Empty repo or branch missing — treat all as additions.
        }
        return map;
    }

    /// <summary>
    /// Computes the Git blob SHA-1 of a file: sha1("blob " + size + "\0" + content).
    /// </summary>
    private static async Task<string> ComputeGitBlobShaAsync(string path)
    {
        var content = await File.ReadAllBytesAsync(path);
        var header = Encoding.UTF8.GetBytes($"blob {content.Length}\0");
        var combined = new byte[header.Length + content.Length];
        Buffer.BlockCopy(header, 0, combined, 0, header.Length);
        Buffer.BlockCopy(content, 0, combined, header.Length, content.Length);
        var hash = SHA1.HashData(combined);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateFiles(string root, GitignoreMatcher matcher)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name == ".git") continue;
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
}
