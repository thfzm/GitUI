using System;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace GitUI.Services;

public record CloneProgress(int Percent, string Message);

public class CloneService
{
    private readonly string _token;
    private readonly string _username;

    public CloneService(string token, string username)
    {
        _token = token;
        _username = username;
    }

    public async Task CloneAsync(
        string url,
        string targetPath,
        string? branch,
        bool recursive,
        IProgress<CloneProgress>? progress,
        CancellationToken ct = default)
    {
        var options = new CloneOptions
        {
            BranchName = branch,
            RecurseSubmodules = recursive
        };
        options.FetchOptions.CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials
        {
            Username = _username,
            Password = _token
        };

        options.FetchOptions.OnTransferProgress = tp =>
        {
            int percent = tp.TotalObjects > 0
                ? (int)((double)tp.ReceivedObjects / tp.TotalObjects * 100)
                : 0;
            var label = $"객체 {tp.ReceivedObjects}/{tp.TotalObjects}";
            if (tp.ReceivedBytes > 0)
            {
                var kb = tp.ReceivedBytes / 1024;
                label += kb < 1024 ? $" · {kb} KB" : $" · {kb / 1024.0:0.#} MB";
            }
            progress?.Report(new CloneProgress(percent, label));
            return !ct.IsCancellationRequested;
        };

        options.OnCheckoutProgress = (path, completed, total) =>
        {
            int percent = total > 0
                ? (int)((double)completed / total * 100)
                : 0;
            progress?.Report(new CloneProgress(percent, $"체크아웃 {completed}/{total} · {path}"));
        };

        await Task.Run(() =>
        {
            try
            {
                Repository.Clone(url, targetPath, options);
            }
            catch (UserCancelledException) { /* user clicked cancel */ }
        }, ct);
    }
}
