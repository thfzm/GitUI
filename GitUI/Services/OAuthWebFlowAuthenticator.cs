using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GitUI.Services;

/// <summary>
/// OAuth 2.0 Web Application Flow for desktop apps:
/// 1. Spin up a local HttpListener on http://localhost:8765/callback/
/// 2. Open https://github.com/login/oauth/authorize in the user's default browser
/// 3. After the user clicks "Authorize" on GitHub, GitHub redirects to our localhost URL with ?code=...
/// 4. Exchange the code (+ client_secret) for an access token
/// </summary>
public class OAuthWebFlowAuthenticator
{
    private static readonly HttpClient Http = new();
    private readonly string _clientId;
    private readonly string _clientSecret;

    public OAuthWebFlowAuthenticator(string clientId, string clientSecret)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public async Task<string?> AuthenticateAsync(string scope = "repo,delete_repo,read:user", CancellationToken ct = default)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(OAuthConfig.RedirectUri);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"localhost:{OAuthConfig.RedirectPort} 포트를 열 수 없습니다. " +
                "다른 프로그램이 사용 중이거나 권한 문제일 수 있습니다.", ex);
        }

        try
        {
            var state = Guid.NewGuid().ToString("N");
            var authorizeUrl =
                "https://github.com/login/oauth/authorize" +
                $"?client_id={Uri.EscapeDataString(_clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(OAuthConfig.RedirectUri)}" +
                $"&scope={Uri.EscapeDataString(scope)}" +
                $"&state={state}";

            try
            {
                Process.Start(new ProcessStartInfo { FileName = authorizeUrl, UseShellExecute = true });
            }
            catch
            {
                throw new InvalidOperationException("브라우저를 여는 데 실패했습니다.");
            }

            // Wait for the GitHub redirect (cancellable / timeout 5 min).
            var ctxTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), ct);
            var winner = await Task.WhenAny(ctxTask, timeoutTask);
            if (winner != ctxTask) return null;

            var ctx = await ctxTask;
            var query = ParseQuery(ctx.Request.Url?.Query ?? "");
            query.TryGetValue("code", out var code);
            query.TryGetValue("state", out var returnedState);
            query.TryGetValue("error", out var error);

            await RespondAsync(ctx, error == null && !string.IsNullOrEmpty(code));

            if (!string.IsNullOrEmpty(error)) return null;
            if (returnedState != state) return null;
            if (string.IsNullOrEmpty(code)) return null;

            // Exchange the code for an access token.
            var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["code"] = code!,
                    ["redirect_uri"] = OAuthConfig.RedirectUri
                })
            };
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("User-Agent", "GitUI");

            var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("access_token", out var tok) ? tok.GetString() : null;
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var s = query.TrimStart('?');
        if (string.IsNullOrEmpty(s)) return result;
        foreach (var part in s.Split('&'))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = Uri.UnescapeDataString(part[..idx]);
            var val = Uri.UnescapeDataString(part[(idx + 1)..]);
            result[key] = val;
        }
        return result;
    }

    private static async Task RespondAsync(HttpListenerContext ctx, bool success)
    {
        var html = success
            ? "<!doctype html><html><head><meta charset=utf-8><title>GitUI</title>" +
              "<style>body{font-family:-apple-system,Segoe UI,sans-serif;background:#0d1117;color:#e6edf3;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}" +
              ".card{background:#161b22;border:1px solid #30363d;border-radius:12px;padding:32px 40px;text-align:center}" +
              "h2{margin:0 0 8px}p{opacity:.7;margin:0}</style></head>" +
              "<body><div class=card><h2>✅ 인증 완료</h2><p>이 창은 자동으로 닫힙니다.</p></div>" +
              "<script>setTimeout(()=>window.close(),1200)</script></body></html>"
            : "<!doctype html><html><body><h2>인증 실패</h2><p>앱으로 돌아가서 다시 시도하세요.</p></body></html>";

        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.StatusCode = success ? 200 : 400;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }
}
