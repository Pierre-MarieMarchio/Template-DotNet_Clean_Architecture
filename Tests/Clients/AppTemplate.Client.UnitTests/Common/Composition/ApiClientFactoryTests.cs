using System.Net;
using AppTemplate.Client.Common.Composition;
using AppTemplate.Client.UnitTests.TestSupport;
using Microsoft.Kiota.Abstractions.Authentication;
using Shouldly;
using Xunit;

namespace AppTemplate.Client.UnitTests.Common.Composition;

public sealed class ApiClientFactoryTests
{
    private const string _purge = """{"deleted":3}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheClient_AddressesTheBaseAddressOfItsHttpClient()
    {
        var api = new StubApi(_ => Task.FromResult(StubApi.Json(HttpStatusCode.OK, _purge)));
        using var http = new HttpClient(api) { BaseAddress = new Uri("https://api.test/") };

        await ApiClientFactory.Create(http).Api.V1.Maintenance.IdempotencyKeys.Expired.DeleteAsync(cancellationToken: Cancellation);

        var sent = api.Sent.ShouldHaveSingleItem();
        sent.Path.ShouldBe("/api/v1/maintenance/idempotency-keys/expired");
        sent.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task AnAccessTokenProvider_AuthorizesEveryRequest()
    {
        var api = new StubApi(_ => Task.FromResult(StubApi.Json(HttpStatusCode.OK, _purge)));
        using var http = new HttpClient(api) { BaseAddress = new Uri("https://api.test/") };

        await ApiClientFactory.Create(http, new FixedAccessToken("external-token"))
            .Api.V1.Maintenance.IdempotencyKeys.Expired.DeleteAsync(cancellationToken: Cancellation);

        api.Sent.ShouldHaveSingleItem().Authorization.ShouldBe("Bearer external-token");
    }

    private sealed class FixedAccessToken(string token) : IAccessTokenProvider
    {
        public AllowedHostsValidator AllowedHostsValidator { get; } = new(["api.test"]);

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }
}
