using System.Security.Claims;
using System.Text.Encodings.Web;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.PresentationAppMcp.Common.Security;

internal sealed class AccessKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthenticateAccessKeyUseCase accessKeys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "AccessKey";

    internal const string AccessKeyClaim = "access_key_id";

    private const string _bearer = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authorization = Request.Headers.Authorization;

        if (authorization is null || !authorization.StartsWith(_bearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var outcome = await accessKeys.ExecuteAsync(
            new AuthenticateAccessKeyCommand(authorization[_bearer.Length..].Trim()),
            Context.RequestAborted);

        if (outcome.IsFailure)
        {
            return AuthenticateResult.Fail(outcome.Error!.Message);
        }

        var caller = outcome.Value;
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, caller.UserId.ToString()),
                new Claim(AccessKeyClaim, caller.AccessKeyId.ToString()),
                .. caller.Scopes.Select(scope => new Claim(ToolPolicies.ScopeClaim, scope)),
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";

        return Task.CompletedTask;
    }
}
