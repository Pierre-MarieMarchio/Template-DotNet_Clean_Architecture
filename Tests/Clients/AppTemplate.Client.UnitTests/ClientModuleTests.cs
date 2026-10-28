using System.Net;
using AppTemplate.Client.Common.Sessions;
//#if (externalAuth)
using AppTemplate.Client.Generated;
//#endif
using AppTemplate.Client.UnitTests.TestSupport;
//#if (externalAuth)
using Microsoft.Extensions.DependencyInjection;
//#endif
using Shouldly;
using Xunit;

namespace AppTemplate.Client.UnitTests;

public sealed class ClientModuleTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATransientFailure_IsRetriedByTheStandardResilienceHandler()
    {
        int attempts = 0;
        var api = new StubApi(_ => Task.FromResult(
            Interlocked.Increment(ref attempts) == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : StubApi.Json(HttpStatusCode.OK, """{"deleted":3}""")));
        using var harness = new ClientHarness(api);

        var purged = await harness.Client.Api.V1.Maintenance.IdempotencyKeys.Expired.DeleteAsync(cancellationToken: Cancellation);

        purged.ShouldNotBeNull().Deleted.ShouldBe(3);
        api.Sent.Count.ShouldBe(2);
    }
    //#if (localAuth)

    [Fact]
    public void TheRefreshToken_IsHeldInMemory_InTheBodyMode_Unless_TheHostChoosesAStore()
    {
        using var byDefault = new ClientHarness(new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        using var chosen = new ClientHarness(
            new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
            new CookieRefreshTokenStore());

        byDefault.RefreshTokens.ShouldBeOfType<InMemoryRefreshTokenStore>().Mode.ShouldBe(RefreshTokenMode.Body);
        chosen.Session.Mode.ShouldBe(RefreshTokenMode.Cookie);
    }
    //#endif
    //#if (externalAuth)

    [Fact]
    public void AHostThatRegistersNoAccessTokenSource_IsToldSoAtFirstUse()
    {
        var services = new ServiceCollection();
        services.AddApiClient(ClientHarness.BaseAddress);
        using var provider = services.BuildServiceProvider();

        var refusal = Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<ApiClient>());

        refusal.Message.ShouldContain(nameof(IAccessTokenSource));
    }
    //#endif
}
