using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUI.Models;
using GitUI.Services;
using Microsoft.Win32;
using Octokit;

namespace GitUI.ViewModels.Tabs;

public partial class FileTreeNode : ObservableObject
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsFolder { get; init; }
    public string? Sha { get; init; }
    public long Size { get; init; }
    public ObservableCollection<FileTreeNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public string Icon => IsFolder ? "📁" : GetFileIcon(Name);

    private static string GetFileIcon(string name)
    {
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".txt" or ".rst" => "📄",
            ".cs" or ".java" or ".go" or ".rs" or ".cpp" or ".c" or ".h" => "📝",
            ".js" or ".ts" or ".jsx" or ".tsx" or ".py" or ".rb" => "📝",
            ".html" or ".htm" or ".xml" or ".xaml" => "🌐",
            ".json" or ".yaml" or ".yml" or ".toml" => "⚙",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" => "🖼",
            ".zip" or ".tar" or ".gz" or ".7z" => "📦",
            ".pdf" => "📕",
            _ => "📄"
        };
    }
}

public partial class FilesTabViewModel : ObservableObject
{
    private readonly GitHubService _github;
    public RepoItem Repo { get; }
    public Func<string> CurrentBranchAccessor { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private FileTreeNode? _selectedNode;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private string? _previewInfo;
    [ObservableProperty] private bool _isPreviewBinary;

    public ObservableCollection<FileTreeNode> RootNodes { get; } = new();

    public FilesTabViewModel(GitHubService github, RepoItem repo, Func<string> currentBranch)
    {
        _github = github;
        Repo = repo;
        CurrentBranchAccessor = currentBranch;
    }

    [RelayCommand]
    public async Task LoadTreeAsync()
    {
        IsBusy = true;
        StatusMessage = "트리 불러오는 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var tree = await _github.GetRepoTreeAsync(owner, Repo.Name, CurrentBranchAccessor());
            BuildTree(tree.Tree);
            Loaded = true;
            StatusMessage = $"파일 {tree.Tree.Count(t => t.Type.Value == TreeType.Blob)}개";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildTree(IReadOnlyList<TreeItem> items)
    {
        RootNodes.Clear();
        var folderMap = new Dictionary<string, FileTreeNode>();

        // Sort: folders before files, then alphabetical
        var sorted = items.OrderBy(i => i.Path.Count(c => c == '/'))
                          .ThenBy(i => i.Type.Value == TreeType.Tree ? 0 : 1)
                          .ThenBy(i => i.Path);

        foreach (var item in sorted)
        {
            var node = new FileTreeNode
            {
                Name = System.IO.Path.GetFileName(item.Path),
                Path = item.Path,
                IsFolder = item.Type.Value == TreeType.Tree,
                Sha = item.Sha,
                Size = item.Size
            };

            var parentPath = System.IO.Path.GetDirectoryName(item.Path)?.Replace('\\', '/') ?? "";
            if (string.IsNullOrEmpty(parentPath))
                RootNodes.Add(node);
            else if (folderMap.TryGetValue(parentPath, out var parent))
                parent.Children.Add(node);

            if (node.IsFolder) folderMap[item.Path] = node;
        }
    }

    partial void OnSelectedNodeChanged(FileTreeNode? value)
    {
        PreviewText = null;
        PreviewInfo = null;
        IsPreviewBinary = false;
        if (value == null || value.IsFolder) return;
        _ = LoadPreviewAsync(value);
    }

    private async Task LoadPreviewAsync(FileTreeNode node)
    {
        const long MaxPreviewBytes = 512 * 1024; // 512 KB

        if (node.Size > MaxPreviewBytes)
        {
            IsPreviewBinary = true;
            PreviewInfo = $"파일이 큽니다 ({FormatSize(node.Size)}). 다운로드해서 확인하세요.";
            return;
        }

        IsBusy = true;
        try
        {
            byte[] bytes;
            if (!string.IsNullOrEmpty(node.Sha))
            {
                var owner = Repo.FullName.Split('/')[0];
                bytes = await _github.GetBlobBytesAsync(owner, Repo.Name, node.Sha);
            }
            else
            {
                var owner = Repo.FullName.Split('/')[0];
                bytes = await _github.GetFileBytesAsync(owner, Repo.Name, node.Path, CurrentBranchAccessor());
            }

            if (LooksBinary(bytes))
            {
                IsPreviewBinary = true;
                PreviewInfo = $"바이너리 파일 · {FormatSize(node.Size)}";
            }
            else
            {
                PreviewText = Encoding.UTF8.GetString(bytes);
                IsPreviewBinary = false;
            }
        }
        catch (Exception ex)
        {
            PreviewInfo = "미리보기 실패: " + ex.Message;
            IsPreviewBinary = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool LooksBinary(byte[] bytes)
    {
        // Heuristic: presence of NUL byte in first 8KB usually means binary
        var sampleLen = Math.Min(8192, bytes.Length);
        for (int i = 0; i < sampleLen; i++)
            if (bytes[i] == 0) return true;
        return false;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.##} KB";
        return $"{bytes / 1024.0 / 1024.0:0.##} MB";
    }

    // ---- Downloads ----------------------------------------------------------

    [RelayCommand]
    private async Task DownloadFileAsync()
    {
        if (SelectedNode == null || SelectedNode.IsFolder) return;
        var dlg = new SaveFileDialog
        {
            FileName = SelectedNode.Name,
            Filter = "All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        StatusMessage = $"'{SelectedNode.Name}' 다운로드 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var bytes = !string.IsNullOrEmpty(SelectedNode.Sha)
                ? await _github.GetBlobBytesAsync(owner, Repo.Name, SelectedNode.Sha!)
                : await _github.GetFileBytesAsync(owner, Repo.Name, SelectedNode.Path, CurrentBranchAccessor());
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            StatusMessage = $"저장됨: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadFolderAsync()
    {
        if (SelectedNode == null || !SelectedNode.IsFolder) return;
        var dlg = new SaveFileDialog
        {
            FileName = $"{SelectedNode.Name}.zip",
            Filter = "ZIP archive|*.zip"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var files = CollectFiles(SelectedNode);
            using var zipStream = new FileStream(dlg.FileName, System.IO.FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                StatusMessage = $"{i + 1}/{files.Count} · {f.Path}";
                var bytes = !string.IsNullOrEmpty(f.Sha)
                    ? await _github.GetBlobBytesAsync(owner, Repo.Name, f.Sha!)
                    : await _github.GetFileBytesAsync(owner, Repo.Name, f.Path, CurrentBranchAccessor());
                var rel = System.IO.Path.GetRelativePath(SelectedNode.Path, f.Path).Replace('\\', '/');
                var entry = archive.CreateEntry($"{SelectedNode.Name}/{rel}", CompressionLevel.Optimal);
                using var es = entry.Open();
                await es.WriteAsync(bytes);
            }
            StatusMessage = $"ZIP 저장됨: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadRepoZipAsync()
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"{Repo.Name}-{CurrentBranchAccessor()}.zip",
            Filter = "ZIP archive|*.zip"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        StatusMessage = "전체 ZIP 다운로드 중...";
        try
        {
            var owner = Repo.FullName.Split('/')[0];
            var bytes = await _github.DownloadRepoArchiveAsync(owner, Repo.Name, CurrentBranchAccessor());
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            StatusMessage = $"저장됨: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<FileTreeNode> CollectFiles(FileTreeNode root)
    {
        var result = new List<FileTreeNode>();
        var stack = new Stack<FileTreeNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var c in n.Children)
            {
                if (c.IsFolder) stack.Push(c);
                else result.Add(c);
            }
        }
        return result;
    }
}
