using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitUI.Services;

public record OAuthSettings(string? ClientId, string? ClientSecret)
{
    public bool HasDeviceFlow => !string.IsNullOrWhiteSpace(ClientId);
    public bool HasWebFlow => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public static class OAuthConfig
{
    /// <summary>
    /// Fixed port the local callback listener binds to. The user must register
    /// http://localhost:8765/callback as the Authorization callback URL on their OAuth App.
    /// </summary>
    public const int RedirectPort = 8765;
    public static string RedirectUri => $"http://localhost:{RedirectPort}/callback/";

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitUI");
    private static readonly string ConfigPath = Path.Combine(Dir, "oauth.dat");

    public static OAuthSettings Load()
    {
        if (!File.Exists(ConfigPath)) return new OAuthSettings(null, null);
        try
        {
            var encrypted = File.ReadAllBytes(ConfigPath);
            var data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<OAuthSettings>(json) ?? new OAuthSettings(null, null);
        }
        catch
        {
            return new OAuthSettings(null, null);
        }
    }

    public static void Save(OAuthSettings settings)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(settings);
        var data = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ConfigPath, encrypted);
    }
}
