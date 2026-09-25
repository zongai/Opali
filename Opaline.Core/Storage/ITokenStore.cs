using Opaline.Core.Auth;

namespace Opaline.Core.Storage;

public interface ITokenStore
{
    OAuthTokens? Load();
    void Save(OAuthTokens tokens);
    void Clear();
}
