using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GitUI.Services;

/// <summary>
/// Application-lifetime singleton that owns all active folder watchers.
/// Survives window close — keeps watching as long as the app process is alive.
/// Configs are persisted to %APPDATA%\GitUI\watches.json so they auto-resume on next launch.
/// </summary>
public sealed class WatchManager
{
    public static WatchManager Instance { get; } = new();

    private readonly Dictionary<string, ActiveWatch> _watches = new();
    private GitHubService? _github;

    public IReadOnlyDictionary<string, ActiveWatch> Watches => _watches;
    public int Count => _watches.Count;

    /// <summary>Fires when the set of active watches changes.</summary>
    public event Action? Changed;

    public sealed class ActiveWatch : IDisposable
    {
        public WatchConfig Config { get; init; } = null!;
        public FolderWatcherService Watcher { get; init; } = null!;
        public ObservableCollection<string> Log { get; } = new();
        public bool IsBusy;
        public DateTime? LastSyncedAt;

        public void Dispose() => Watcher.Dispose();
    }

    public void Bind(GitHubService github) => _github = github;

    public bool IsWatching(string id) => _watches.ContainsKey(id);
    public ActiveWatch? Get(string id) => _watches.TryGetValue(id, out var v) ? v : null;

    public void Start(WatchConfig config)
    {
        if (_watches.ContainsKey(config.Id)) return;
        if (!Directory.Exists(config.FolderPath)) return;

        var watcher = new FolderWatcherService { DebounceMilliseconds = Math.Max(1, config.DebounceSeconds) * 1000 };
        var aw = new ActiveWatch { Config = config, Watcher = watcher };
        watcher.FileChanged += path => HandleFileChanged(aw, path);
        watcher.ChangesSettled += () => _ = HandleSettledAsync(aw);
        watcher.Start(config.FolderPath);
        _watches[config.Id] = aw;
        Append(aw, $"[감시 시작] {config.FolderPath}");
        PersistAndNotify();
    }

    public void Stop(string id)
    {
        if (!_watches.TryGetValue(id, out var aw)) return;
        aw.Dispose();
        _watches.Remove(id);
        PersistAndNotify();
    }

    public void StopAll()
    {
        foreach (var aw in _watches.Values) aw.Dispose();
        _watches.Clear();
        PersistAndNotify();
    }

    /// <summary>
    /// On startup (after auth), restore all previously-saved watch configs.
    /// </summary>
    public void ResumeAll()
    {
        foreach (var config in WatchConfigStorage.Load())
        {
            try { Start(config); } catch { /* skip broken */ }
        }
    }

    private void HandleFileChanged(ActiveWatch aw, string path)
    {
        try
        {
            var rel = Path.GetRelativePath(aw.Config.FolderPath, path);
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
                dispatcher.Invoke(() => Append(aw, $"변경: {rel}"));
            else
                Append(aw, $"변경: {rel}");
        }
        catch { }
    }

    private async Task HandleSettledAsync(ActiveWatch aw)
    {
        if (aw.IsBusy) return;
        if (_github == null || !_github.IsAuthenticated)
        {
            Dispatch(() => Append(aw, "[인증 안됨, 동기화 보류]"));
            return;
        }
        Dispatch(() => Append(aw, "[디바운스 완료, 동기화 시작]"));
        aw.IsBusy = true;
        try
        {
            var parts = aw.Config.RepoFullName.Split('/');
            if (parts.Length != 2) return;
            var owner = parts[0];
            var name = parts[1];
            var prefix = aw.Config.TargetSubpath?.Trim().Trim('/') ?? "";

            var preview = await SyncPreview.ComputeAsync(
                _github.Client!, owner, name, aw.Config.DefaultBranch,
                aw.Config.FolderPath, prefix);

            int n = 0;
            foreach (var entry in preview.Entries)
            {
                if (entry.Change is not (FileChange.Added or FileChange.Modified)) continue;
                var bytes = await File.ReadAllBytesAsync(entry.LocalPath);
                await _github.UploadFileAsync(owner, name, entry.TargetPath, bytes,
                    aw.Config.CommitMessage, aw.Config.DefaultBranch);
                n++;
            }
            aw.LastSyncedAt = DateTime.Now;
            Dispatch(() => Append(aw, n > 0
                ? $"[동기화 완료] {n}개 파일 ({preview.Unchanged}개 변경 없음)"
                : "[변경 없음]"));
        }
        catch (Exception ex)
        {
            Dispatch(() => Append(aw, $"[오류] {ex.Message}"));
        }
        finally
        {
            aw.IsBusy = false;
        }
    }

    private static void Dispatch(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d != null) d.Invoke(action);
        else action();
    }

    private static void Append(ActiveWatch aw, string line)
    {
        aw.Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (aw.Log.Count > 200) aw.Log.RemoveAt(aw.Log.Count - 1);
    }

    private void PersistAndNotify()
    {
        WatchConfigStorage.Save(_watches.Values.Select(v => v.Config));
        try { Changed?.Invoke(); } catch { }
    }
}
