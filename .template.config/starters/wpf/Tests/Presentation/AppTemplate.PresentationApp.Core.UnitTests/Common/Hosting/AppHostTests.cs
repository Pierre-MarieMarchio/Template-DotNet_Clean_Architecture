using System.Net;
using AppTemplate.Client.Generated;
//#if (externalAuth)
using AppTemplate.PresentationApp.Core.Common.Sessions;
//#endif
using AppTemplate.PresentationApp.Core.UnitTests.TestSupport;
using AppTemplate.PresentationApp.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.Core.UnitTests.Common.Hosting;

public sealed class AppHostTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheHost_RefusesToStart_WithoutAnAbsoluteApiAddress()
    {
        using var host = PresentationHost.Build(new StubApi(_ => new HttpResponseMessage(HttpStatusCode.OK)), baseAddress: "/api/");

        await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(Cancellation));
    }

    [Fact]
    public async Task TheMainViewModel_AndTheClient_ComposeAgainstTheConfiguredAddress()
    {
        using var host = PresentationHost.Build(new StubApi(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        await host.StartAsync(Cancellation);

        host.Services.GetRequiredService<MainViewModel>().ApiAddress.ShouldBe(PresentationHost.BaseAddress);
        host.Services.GetRequiredService<ApiClient>().ShouldNotBeNull();

        await host.StopAsync(Cancellation);
    }
    //#if (externalAuth)

    [Fact]
    public async Task TheHost_RefusesToStart_UntilTheSignInIsConfigured()
    {
        using var host = PresentationHost.Build(new StubApi(_ => new HttpResponseMessage(HttpStatusCode.OK)), authority: string.Empty);

        var refused = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(Cancellation));

        refused.Message.ShouldContain("'SignIn:Authority'");
    }

    [Fact]
    public async Task TheClient_CallsTheApi_WithTheSessionsAccessToken()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", "r1"));
        var presented = new List<string?>();
        var api = new StubApi(request =>
        {
            if (provider.Answer(request) is { } answer)
            {
                return answer;
            }

            presented.Add(request.Headers.Authorization?.ToString());

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var host = PresentationHost.Build(api);
        await host.StartAsync(Cancellation);
        var client = host.Services.GetRequiredService<ApiClient>();

        await client.Api.V1.ExternalIdentities[Guid.NewGuid()].DeleteAsync(cancellationToken: Cancellation);
        await host.Services.GetRequiredService<DesktopSession>().SignInAsync(Cancellation);
        await client.Api.V1.ExternalIdentities[Guid.NewGuid()].DeleteAsync(cancellationToken: Cancellation);

        presented.ShouldBe([null, "Bearer a1"]);

        await host.StopAsync(Cancellation);
    }
    //#endif
}
