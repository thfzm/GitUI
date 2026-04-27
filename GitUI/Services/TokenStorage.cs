using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitUI.Services;

public enum AuthMethod { Pat, OAuthDeviceFlow, OAuthWebFlow }

public record StoredAuth(string Token, AuthMethod Method, string? Username);

public static class TokenStorage
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitUI");
    private static readonly string TokenPath = Path.Combine(Dir, "token.dat");

    public static void Save(StoredAuth auth)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(auth);
        var data = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, encrypted);
    }

    public static StoredAuth? Load()
    {
        if (!File.Exists(TokenPath)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(TokenPath);
            var data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<StoredAuth>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        if (File.Exists(TokenPath)) File.Delete(TokenPath);
    }
}
