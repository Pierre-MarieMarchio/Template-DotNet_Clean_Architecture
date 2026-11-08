using AppTemplate.FrameworkSite.Common.Sessions;
using AppTemplate.FrameworkSite.UnitTests.TestSupport;
using Shouldly;
using Xunit;

namespace AppTemplate.FrameworkSite.UnitTests.Common.Sessions;

public sealed class ProviderAccessTokenSourceTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheAccessToken_IsTheOneTheProviderIssued()
    {
        var source = new ProviderAccessTokenSource(new StubAccessTokenProvider { AccessToken = "a1" });

        (await source.AccessTokenAsync(Cancellation)).ShouldBe("a1");
    }

    [Fact]
    public async Task NoAccessToken_IsGiven_BeforeSignIn()
    {
        var source = new ProviderAccessTokenSource(new StubAccessTokenProvider());

        (await source.AccessTokenAsync(Cancellation)).ShouldBeNull();
    }
}
