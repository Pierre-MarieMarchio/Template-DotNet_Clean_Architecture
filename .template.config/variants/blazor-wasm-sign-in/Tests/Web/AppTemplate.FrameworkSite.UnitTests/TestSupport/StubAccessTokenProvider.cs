using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace AppTemplate.FrameworkSite.UnitTests.TestSupport;

internal sealed class StubAccessTokenProvider : IAccessTokenProvider
{
    public string? AccessToken { get; set; }

    public ValueTask<AccessTokenResult> RequestAccessToken() => ValueTask.FromResult(Result());

    public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options) => ValueTask.FromResult(Result());

    private AccessTokenResult Result() =>
        AccessToken is null
            ? new AccessTokenResult(AccessTokenResultStatus.RequiresRedirect, null!, "authentication/login", null)
            : new AccessTokenResult(
                AccessTokenResultStatus.Success,
                new AccessToken { Value = AccessToken, Expires = DateTimeOffset.UtcNow.AddMinutes(5), GrantedScopes = ["openid"] },
                null,
                null);
}
