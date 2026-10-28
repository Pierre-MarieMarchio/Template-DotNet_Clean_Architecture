using System.Net;
using AppTemplate.Client.Generated;
using AppTemplate.PresentationApp.UnitTests.TestSupport;
using AppTemplate.PresentationApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Common.Hosting;

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
}
