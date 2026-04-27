using System;
using System.IO;
using System.Threading;

namespace GitUI.Services;

/// <summary>
/// Wraps FileSystemWatcher with debouncing — fires <see cref="ChangesSettled"/>
/// after activity stops for <see cref="DebounceMilliseconds"/>.
/// </summary>
public sealed class FolderWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private readonly object _lock = new();

    public int DebounceMilliseconds { get; set; } = 3000;

    public event Action? ChangesSettled;
    public event Action<string>? FileChanged;

    public string? FolderPath { get; private set; }
    public bool IsRunning => _watcher != null;

    public void Start(string folderPath)
    {
        Stop();
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        FolderPath = folderPath;
        _watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size
        };
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += (_, e) => OnChanged(_, new FileSystemEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(e.FullPath) ?? "", Path.GetFileName(e.FullPath)));
        _watcher.Deleted += OnChanged;
        _watcher.Error += (_, e) => { /* swallow */ };
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            FolderPath = null;
        }
    }

    private void OnChanged(object? sender, FileSystemEventArgs e)
    {
        // Skip noise
        var path = e.FullPath.Replace('\\', '/');
        if (path.Contains("/.git/", StringComparison.OrdinalIgnoreCase)) return;

        FileChanged?.Invoke(e.FullPath);

        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                try { ChangesSettled?.Invoke(); } catch { }
            }, null, DebounceMilliseconds, Timeout.Infinite);
        }
    }

    public void Dispose() => Stop();
}
