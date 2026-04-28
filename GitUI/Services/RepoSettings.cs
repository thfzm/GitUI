using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GitUI.Services;

/// <summary>
/// Per-repository preferences (last selected upload folder, watch folder, options, etc.)
/// Persisted across restarts to %APPDATA%\GitUI\repo-settings.json.
/// </summary>
public class RepoPreferences
{
    public string? UploadFolder { get; set; }
    public string? UploadTargetSubpath { get; set; }
    public string? UploadCommitMessage { get; set; }
    public bool UploadRespectGitignore { get; set; } = true;
    public bool UploadMirrorDeletions { get; set; } = true;

    public string? WatchFolder { get; set; }
    public string? WatchTargetSubpath { get; set; }
    public string? WatchCommitMessage { get; set; }
    public int WatchDebounceSeconds { get; set; } = 3;
    public bool WatchRespectGitignore { get; set; } = true;
    public bool WatchMirrorDeletions { get; set; } = true;
}

public static class RepoSettingsStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitUI");
    private static readonly string FilePath = Path.Combine(Dir, "repo-settings.json");

    private static Dictionary<string, RepoPreferences>? _cache;
    private static readonly object _lock = new();

    private static Dictionary<string, RepoPreferences> Cache()
    {
        if (_cache != null) return _cache;
        var dict = new Dictionary<string, RepoPreferences>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, RepoPreferences>>(json);
                if (loaded != null)
                    dict = new Dictionary<string, RepoPreferences>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        _cache = dict;
        return _cache;
    }

    /// <summary>
    /// Returns the prefs for a repo. Mutating the returned object affects the cache;
    /// call <see cref="Save"/> to persist.
    /// </summary>
    public static RepoPreferences Get(string repoFullName)
    {
        lock (_lock)
        {
            var d = Cache();
            if (!d.TryGetValue(repoFullName, out var prefs))
            {
                prefs = new RepoPreferences();
                d[repoFullName] = prefs;
            }
            return prefs;
        }
    }

    public static void Save()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(Cache(),
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
