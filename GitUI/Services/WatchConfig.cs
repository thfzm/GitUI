using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GitUI.Services;

public record WatchConfig(
    string Id,
    string RepoFullName,
    string DefaultBranch,
    string FolderPath,
    string CommitMessage,
    string TargetSubpath,
    int DebounceSeconds,
    bool RespectGitignore);

public static class WatchConfigStorage
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitUI");
    private static readonly string FilePath = Path.Combine(Dir, "watches.json");

    public static List<WatchConfig> Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var raw = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<WatchConfig>>(raw) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Save(IEnumerable<WatchConfig> configs)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(configs.ToArray(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
