using System.Net;
using AppTemplate.Client.Generated.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace AppTemplate.Client.Common.Sessions;

public sealed class ApiSession(IHttpClientFactory httpClients, IRefreshTokenStore refreshTokens)
{
    internal const string ModeHeader = "X-Refresh-Token-Mode";

    internal const string CookieMode = "cookie";

    private const string _jsonContentType = "application/json";

    internal const string RefreshPath = "/api/v1/auth/refresh";

    internal const string LogoutPath = "/api/v1/auth/logout";

    private readonly Lock _gate = new();
    private string? _accessToken;
    private int _generation;
    private Task<bool>? _refresh;

    public string? AccessToken
    {
        get
        {
            lock (_gate)
            {
                return _accessToken;
            }
        }
    }

    public bool IsSignedIn => AccessToken is not null;

    public RefreshTokenMode Mode => refreshTokens.Mode;

    public async Task SignedInAsync(TokenResponse tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (string.IsNullOrEmpty(tokens.AccessToken))
        {
            throw new ArgumentException("A sign-in answer carries an access token; this one is empty.", nameof(tokens));
        }

        lock (_gate)
        {
            _generation++;
            _accessToken = tokens.AccessToken;
        }

        await refreshTokens.WriteAsync(tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_refresh is not { IsCompleted: false })
            {
                int started = _generation;
                _refresh = Task.Run(() => ExchangeAsync(started), CancellationToken.None);
            }

            return _refresh.WaitAsync(cancellationToken);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        Task<bool>? inFlight;

        lock (_gate)
        {
            _generation++;
            _accessToken = null;
            inFlight = _refresh;
        }

        if (inFlight is not null)
        {
            await inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        string? refreshToken = await refreshTokens.ReadAsync(cancellationToken).ConfigureAwait(false);
        await refreshTokens.WriteAsync(null, cancellationToken).ConfigureAwait(false);

        if (Mode is RefreshTokenMode.Body && refreshToken is null)
        {
            return;
        }

        using var request = await SessionRequestAsync(LogoutPath, refreshToken is null ? null : new LogoutRequest { RefreshToken = refreshToken })
            .ConfigureAwait(false);

        try
        {
            using var response = await httpClients.CreateClient(ClientModule.SessionHttpClientName)
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return;
        }
    }

    private async Task<bool> ExchangeAsync(int started)
    {
        string? refreshToken = await refreshTokens.ReadAsync(CancellationToken.None).ConfigureAwait(false);

        if (Mode is RefreshTokenMode.Body && refreshToken is null)
        {
            return false;
        }

        using var request = await SessionRequestAsync(
                RefreshPath,
                refreshToken is null ? null : new RefreshAccessTokenRequest { RefreshToken = refreshToken })
            .ConfigureAwait(false);

        HttpResponseMessage response;

        try
        {
            response = await httpClients.CreateClient(ClientModule.SessionHttpClientName)
                .SendAsync(request, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return false;
        }

        using (response)
        {
            TokenResponse? tokens = response.IsSuccessStatusCode ? await ReadTokensAsync(response).ConfigureAwait(false) : null;

            lock (_gate)
            {
                if (started != _generation)
                {
                    return _accessToken is not null;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized)
                {
                    _accessToken = null;
                }
                else if (tokens is { AccessToken: { Length: > 0 } accessToken })
                {
                    _accessToken = accessToken;
                }
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                await refreshTokens.WriteAsync(null, CancellationToken.None).ConfigureAwait(false);
                return false;
            }

            if (tokens is not { AccessToken.Length: > 0 })
            {
                return false;
            }

            await refreshTokens.WriteAsync(tokens.RefreshToken, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
    }

    private async Task<HttpRequestMessage> SessionRequestAsync(string path, IParsable? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'));

        if (Mode is RefreshTokenMode.Cookie)
        {
            request.Headers.Add(ModeHeader, CookieMode);
        }

        if (body is not null)
        {
            using var writer = new JsonSerializationWriterFactory().GetSerializationWriter(_jsonContentType);
            writer.WriteObjectValue(null, body);

            using var content = writer.GetSerializedContent();
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer).ConfigureAwait(false);
            buffer.Position = 0;

            request.Content = new StreamContent(buffer);
            request.Content.Headers.ContentType = new(_jsonContentType);
        }

        return request;
    }

    private static async Task<TokenResponse?> ReadTokensAsync(HttpResponseMessage response)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var root = await new JsonParseNodeFactory().GetRootParseNodeAsync(_jsonContentType, stream).ConfigureAwait(false);

            return root.GetObjectValue(TokenResponse.CreateFromDiscriminatorValue);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
