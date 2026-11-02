using AppTemplate.Client.Generated;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace AppTemplate.Client.Common.Composition;

public static class ApiClientFactory
{
    public static ApiClient Create(HttpClient httpClient, IAccessTokenProvider? accessTokens = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        IAuthenticationProvider authentication = accessTokens is null
            ? new AnonymousAuthenticationProvider()
            : new BaseBearerTokenAuthenticationProvider(accessTokens);

        return new ApiClient(new HttpClientRequestAdapter(authentication, httpClient: httpClient));
    }
}
