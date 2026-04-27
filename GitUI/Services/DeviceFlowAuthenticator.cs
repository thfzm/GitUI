using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GitUI.Services;

public record DeviceCodeResponse(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);

public class DeviceFlowAuthenticator
{
    private static readonly HttpClient Http = new();
    private readonly string _clientId;

    public DeviceFlowAuthenticator(string clientId) => _clientId = clientId;

    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(string scope = "repo,delete_repo,read:user")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["scope"] = scope
            })
        };
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("User-Agent", "GitUI");
        var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new DeviceCodeResponse(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.GetProperty("expires_in").GetInt32(),
            root.GetProperty("interval").GetInt32()
        );
    }

    /// <summary>
    /// Polls GitHub until the user authorizes (returns access_token) or times out (returns null).
    /// </summary>
    public async Task<string?> PollForTokenAsync(DeviceCodeResponse code, CancellationToken ct = default)
    {
        var interval = code.Interval;
        var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(interval), ct); }
            catch (TaskCanceledException) { return null; }

            var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _clientId,
                    ["device_code"] = code.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                })
            };
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("User-Agent", "GitUI");

            try
            {
                var resp = await Http.SendAsync(req, ct);
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("access_token", out var tok))
                    return tok.GetString();

                if (root.TryGetProperty("error", out var err))
                {
                    var e = err.GetString();
                    if (e == "authorization_pending") continue;
                    if (e == "slow_down") { interval += 5; continue; }
                    return null; // expired_token, access_denied, unsupported_grant_type, ...
                }
            }
            catch (TaskCanceledException) { return null; }
        }
        return null;
    }
}
