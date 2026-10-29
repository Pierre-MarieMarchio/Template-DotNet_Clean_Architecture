using AppTemplate.Client.Common.Sessions;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace AppTemplate.FrameworkSite.Common.Sessions;

internal sealed class ProviderAccessTokenSource(IAccessTokenProvider provider) : IAccessTokenSource
{
    public async ValueTask<string?> AccessTokenAsync(CancellationToken cancellationToken)
    {
        AccessTokenResult result = await provider.RequestAccessToken();

        return result.TryGetToken(out AccessToken? token) ? token.Value : null;
    }
}
