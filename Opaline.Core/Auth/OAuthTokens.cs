namespace Opaline.Core.Auth;

public sealed class OAuthTokens
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-2);
}
