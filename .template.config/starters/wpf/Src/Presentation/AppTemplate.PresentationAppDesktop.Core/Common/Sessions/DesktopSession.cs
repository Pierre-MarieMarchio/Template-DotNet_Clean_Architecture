using System.Security.Claims;
using AppTemplate.Client.Common.Sessions;
using AppTemplate.PresentationAppDesktop.Core.Common.Hosting;
using Duende.IdentityModel;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Results;
using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppDesktop.Core.Common.Sessions;

public sealed class DesktopSession(IOptions<SignInOptions> options, ISystemBrowser systemBrowser, IHttpClientFactory httpClients)
    : IAccessTokenSource
{
    public const string HttpClientName = "AppTemplate.PresentationAppDesktop.Core.SignIn";

    public static readonly TimeSpan RefreshAhead = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private Tokens? _tokens;
    private int _generation;
    private Task<string?>? _refresh;

    public bool IsSignedIn
    {
        get
        {
            lock (_gate)
            {
                return _tokens is not null;
            }
        }
    }

    public string? SignedInAs
    {
        get
        {
            lock (_gate)
            {
                return _tokens?.SignedInAs;
            }
        }
    }

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        using var browser = new LoopbackBrowser(systemBrowser);
        var client = new OidcClient(ClientOptions(browser));
        LoginResult result;

        try
        {
            result = await client.LoginAsync(new LoginRequest(), cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException error)
        {
            throw new SignInFailedException(error.Message, error);
        }

        if (result.IsError)
        {
            throw new SignInFailedException(string.IsNullOrEmpty(result.ErrorDescription) ? result.Error : $"{result.Error}: {result.ErrorDescription}");
        }

        lock (_gate)
        {
            _generation++;
            _tokens = new Tokens(
                client,
                result.AccessToken,
                NullIfEmpty(result.RefreshToken),
                result.AccessTokenExpiration,
                NameOf(result.User));
        }
    }

    public void SignOut()
    {
        lock (_gate)
        {
            _generation++;
            _tokens = null;
        }
    }

    public async ValueTask<string?> AccessTokenAsync(CancellationToken cancellationToken)
    {
        Task<string?> refresh;

        lock (_gate)
        {
            if (_tokens is not { } tokens)
            {
                return null;
            }

            if (tokens.ExpiresAt - RefreshAhead > DateTimeOffset.UtcNow)
            {
                return tokens.AccessToken;
            }

            if (tokens.RefreshToken is null)
            {
                return CurrentOrForget(tokens);
            }

            if (_refresh is not { IsCompleted: false })
            {
                int started = _generation;
                _refresh = Task.Run(() => RefreshAsync(tokens, started), CancellationToken.None);
            }

            refresh = _refresh;
        }

        return await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RefreshAsync(Tokens current, int started)
    {
        RefreshTokenResult? result;

        try
        {
            result = await current.Client.RefreshTokenAsync(current.RefreshToken!, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            result = null;
        }

        lock (_gate)
        {
            if (started != _generation)
            {
                return _tokens?.AccessToken;
            }

            if (result is not { IsError: false, AccessToken.Length: > 0 })
            {
                return CurrentOrForget(current);
            }

            _tokens = current with
            {
                AccessToken = result.AccessToken,
                RefreshToken = NullIfEmpty(result.RefreshToken) ?? current.RefreshToken,
                ExpiresAt = result.AccessTokenExpiration,
            };

            return _tokens.AccessToken;
        }
    }

    private string? CurrentOrForget(Tokens tokens)
    {
        if (tokens.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return tokens.AccessToken;
        }

        _tokens = null;

        return null;
    }

    private OidcClientOptions ClientOptions(LoopbackBrowser browser)
    {
        var signIn = options.Value;

        return new OidcClientOptions
        {
            Authority = signIn.Authority!.OriginalString,
            ClientId = signIn.ClientId,
            Scope = signIn.Scope,
            RedirectUri = browser.RedirectUri.AbsoluteUri,
            Browser = browser,
            HttpClientFactory = _ => httpClients.CreateClient(HttpClientName),
            LoadProfile = false,
            Policy = { Discovery = { RequireHttps = signIn.RequireHttpsMetadata } },
        };
    }

    private static string? NameOf(ClaimsPrincipal? user) =>
        (user?.FindFirst(JwtClaimTypes.Name)
            ?? user?.FindFirst(JwtClaimTypes.PreferredUserName)
            ?? user?.FindFirst(JwtClaimTypes.Email)
            ?? user?.FindFirst(JwtClaimTypes.Subject))?.Value;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed record Tokens(OidcClient Client, string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string? SignedInAs);
}
