namespace Opaline.Core.Auth;

public sealed class OAuthTokens
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-2);
}
