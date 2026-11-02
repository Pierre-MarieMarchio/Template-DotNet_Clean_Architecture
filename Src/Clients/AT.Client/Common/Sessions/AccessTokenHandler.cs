using System.Net.Http.Headers;

namespace AppTemplate.Client.Common.Sessions;

internal sealed class AccessTokenHandler(IAccessTokenSource accessTokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? accessToken = await accessTokens.AccessTokenAsync(cancellationToken).ConfigureAwait(false);

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
