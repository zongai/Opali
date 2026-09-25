using System.Text.Json;
using Opaline.Core.Auth;

namespace Opaline.Core.Storage;

/// <summary>
/// File-based token store under %LOCALAPPDATA%/Opaline/oauth.json.
/// </summary>
public sealed class FileTokenStore : ITokenStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    public string Path => _path;

    public FileTokenStore(string? directory = null)
    {
        var dir = directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Opaline");
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "oauth.json");
    }

    public OAuthTokens? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = File.ReadAllText(_path);
            var tokens = JsonSerializer.Deserialize<OAuthTokens>(json, JsonOpts);
            if (tokens is null) return null;
            // Require at least access or refresh to consider valid
            if (string.IsNullOrEmpty(tokens.AccessToken) && string.IsNullOrEmpty(tokens.RefreshToken))
                return null;
            return tokens;
        }
        catch
        {
            return null;
        }
    }

    public void Save(OAuthTokens tokens)
    {
        var json = JsonSerializer.Serialize(tokens, JsonOpts);
        File.WriteAllText(_path, json);
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* ignore */ }
    }
}
