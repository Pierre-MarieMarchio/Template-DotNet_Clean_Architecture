using System.Net;
using Shouldly;
using Xunit;

namespace AppTemplate.EndToEnd.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task ASession_RunsFromTheMailedLinkToSignOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var api = Stack.Client(Stack.Api);
        var account = Accounts.New();

        await Accounts.RegisterAndConfirmAsync(api, account, cancellationToken);
        var signedIn = await Accounts.SignInAsync(api, account, cancellationToken);

        using (var me = await Wire.SendAsync(api, HttpMethod.Get, "api/v1/auth/me", cancellationToken, bearer: signedIn.AccessToken))
        {
            Wire.Text(await Wire.ExpectJsonAsync(me, HttpStatusCode.OK, cancellationToken), "email").ShouldBe(account.Email);
        }

        SignedIn refreshed;
        using (var refresh = await Refresh(api, signedIn.RefreshToken, cancellationToken))
        {
            refreshed = Accounts.Pair(await Wire.ExpectJsonAsync(refresh, HttpStatusCode.OK, cancellationToken));
        }

        refreshed.RefreshToken.ShouldNotBe(signedIn.RefreshToken);

        using (var logout = await Wire.SendAsync(
            api, HttpMethod.Post, "api/v1/auth/logout", cancellationToken, new { refreshToken = refreshed.RefreshToken }))
        {
            await Wire.ExpectAsync(logout, HttpStatusCode.NoContent, cancellationToken);
        }

        using var afterLogout = await Refresh(api, refreshed.RefreshToken, cancellationToken);
        await Wire.ExpectAsync(afterLogout, HttpStatusCode.Unauthorized, cancellationToken);
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient api, string refreshToken, CancellationToken cancellationToken) =>
        Wire.SendAsync(api, HttpMethod.Post, "api/v1/auth/refresh", cancellationToken, new { refreshToken });
}
