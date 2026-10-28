using System.Net;
using AppTemplate.Client.UnitTests.TestSupport;
using Shouldly;
using Xunit;

namespace AppTemplate.Client.UnitTests.Common.Sessions;

public sealed class AccessTokenHandlerTests
{
    private const string _probe = "/api/v1/probe";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequest_CarriesTheAccessTokenTheHostsSourceGives()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var harness = new ClientHarness(api);
        harness.AccessTokens.AccessToken = "a1";

        using var response = await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation);

        api.SentTo(_probe).Single().Authorization.ShouldBe("Bearer a1");
    }

    [Fact]
    public async Task ARequest_CarriesNoAuthorization_WhileTheSourceHasNoToken_AndTheTokenOnceItHasOne()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var harness = new ClientHarness(api);

        using (await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation))
        {
        }

        harness.AccessTokens.AccessToken = "a1";
        using var signedIn = await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation);

        api.SentTo(_probe).Select(request => request.Authorization).ShouldBe([null, "Bearer a1"]);
    }
}
