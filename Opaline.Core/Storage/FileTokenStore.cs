using System.Text.Json;
using Opaline.Core.Auth;

namespace Opaline.Core.Storage;

/// <summary>
/// Simple file-based token store under %LOCALAPPDATA%/Opaline.
/// On Windows the app layer can swap this for Credential Manager / DPAPI.
/// </summary>
public sealed class FileTokenStore : ITokenStore
{
    private readonly string _path;

    public FileTokenStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Opaline");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "oauth.json");
    }

    public OAuthTokens? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<OAuthTokens>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(OAuthTokens tokens)
    {
        var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* ignore */ }
    }
}
