using System.Net;
using System.Net.Http.Headers;

namespace AppTemplate.Client.Common.Sessions;

internal sealed class ApiSessionHandler(ApiSession session) : DelegatingHandler
{
    private static readonly string[] _sessionPaths =
    [
        "/api/v1/auth/login",
        "/api/v1/auth/login/two-factor",
        "/api/v1/auth/login/external",
        "/api/v1/auth/passkeys/sign-in",
        ApiSession.RefreshPath,
        ApiSession.LogoutPath,
    ];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsSessionPath(request.RequestUri))
        {
            if (session.Mode is RefreshTokenMode.Cookie)
            {
                request.Headers.Remove(ApiSession.ModeHeader);
                request.Headers.Add(ApiSession.ModeHeader, ApiSession.CookieMode);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        string? presented = session.AccessToken;
        Authorize(request, presented);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is not HttpStatusCode.Unauthorized)
        {
            return response;
        }

        string? current = session.AccessToken;
        bool renewed = (current is not null && !string.Equals(current, presented, StringComparison.Ordinal))
            || await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        current = session.AccessToken;

        if (!renewed || current is null)
        {
            return response;
        }

        response.Dispose();
        Authorize(request, current);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSessionPath(Uri? requestUri) =>
        requestUri is not null
        && _sessionPaths.Any(path => requestUri.AbsolutePath.EndsWith(path, StringComparison.OrdinalIgnoreCase));

    private static void Authorize(HttpRequestMessage request, string? accessToken) =>
        request.Headers.Authorization = accessToken is null ? null : new AuthenticationHeaderValue("Bearer", accessToken);
}
