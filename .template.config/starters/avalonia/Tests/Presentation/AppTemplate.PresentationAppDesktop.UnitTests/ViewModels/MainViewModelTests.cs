using System.Net;
//#if (localAuth)
using AppTemplate.Client.Common.Sessions;
//#else
using AppTemplate.PresentationApp.Common.Sessions;
//#endif
using AppTemplate.PresentationApp.UnitTests.TestSupport;
using AppTemplate.PresentationApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.ViewModels;

public sealed class MainViewModelTests
{
    //#if (localAuth)
    private const string _login = "/api/v1/auth/login";
    private const string _me = "/api/v1/auth/me";
    private const string _logout = "/api/v1/auth/logout";
    private const string _signedIn = """{"status":"authenticated","tokens":{"accessToken":"a1","refreshToken":"r1","expiresIn":900}}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SigningIn_HoldsTheTokens_AndShowsWhoIsSignedIn()
    {
        var api = new StubApi(request => request.RequestUri!.AbsolutePath switch
        {
            _login => StubApi.Json(HttpStatusCode.OK, _signedIn),
            _me => StubApi.Json(HttpStatusCode.OK, """{"email":"ada@example.com"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var host = PresentationHost.Build(api);
        var main = host.Services.GetRequiredService<MainViewModel>();
        main.Email = "ada@example.com";
        main.Password = "Secret-1";

        await main.SignInCommand.ExecuteAsync(Cancellation);

        main.SignedInAs.ShouldBe("ada@example.com");
        main.Password.ShouldBeEmpty();
        main.Problem.ShouldBeNull();
        host.Services.GetRequiredService<ApiSession>().AccessToken.ShouldBe("a1");
        api.Sent.ShouldBe([_login, _me]);
    }

    [Fact]
    public async Task ARefusedSignIn_ShowsTheProblemCode()
    {
        var api = new StubApi(_ => StubApi.Problem(
            HttpStatusCode.Unauthorized,
            """{"type":"about:blank","status":401,"code":"auth.invalid_credentials","traceId":"t"}"""));
        using var host = PresentationHost.Build(api);
        var main = host.Services.GetRequiredService<MainViewModel>();

        await main.SignInCommand.ExecuteAsync(Cancellation);

        main.Problem.ShouldBe("auth.invalid_credentials");
        main.SignedInAs.ShouldBeNull();
        host.Services.GetRequiredService<ApiSession>().IsSignedIn.ShouldBeFalse();
    }

    [Fact]
    public async Task AnAccountWithASecondFactor_IsNotSignedIn()
    {
        var api = new StubApi(_ => StubApi.Json(HttpStatusCode.OK, """{"status":"twoFactorRequired","challengeToken":"c1"}"""));
        using var host = PresentationHost.Build(api);
        var main = host.Services.GetRequiredService<MainViewModel>();

        await main.SignInCommand.ExecuteAsync(Cancellation);

        main.Problem.ShouldBe(MainViewModel.SecondFactorRequired);
        main.SignedInAs.ShouldBeNull();
    }

    [Fact]
    public async Task SigningOut_ForgetsTheSession_AndTellsTheApi()
    {
        var api = new StubApi(request => request.RequestUri!.AbsolutePath switch
        {
            _login => StubApi.Json(HttpStatusCode.OK, _signedIn),
            _me => StubApi.Json(HttpStatusCode.OK, """{"email":"ada@example.com"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        });
        using var host = PresentationHost.Build(api);
        var main = host.Services.GetRequiredService<MainViewModel>();
        await main.SignInCommand.ExecuteAsync(Cancellation);

        await main.SignOutCommand.ExecuteAsync(Cancellation);

        main.SignedInAs.ShouldBeNull();
        host.Services.GetRequiredService<ApiSession>().IsSignedIn.ShouldBeFalse();
        api.Sent.ShouldContain(_logout);
    }
    //#else
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SigningIn_WaitsOnTheBrowser_ThenShowsWhoIsSignedIn()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", "r1"));
        MainViewModel? main = null;
        string? statusWhileRedeeming = null;
        var api = new StubApi(request =>
        {
            if (request.RequestUri!.AbsolutePath == StubProvider.TokenPath)
            {
                statusWhileRedeeming = main!.Status;
            }

            return provider.Answer(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var host = PresentationHost.Build(api);
        main = host.Services.GetRequiredService<MainViewModel>();

        await main.SignInCommand.ExecuteAsync(Cancellation);

        statusWhileRedeeming.ShouldBe(MainViewModel.FinishInTheBrowser);
        main.SignedInAs.ShouldBe("Ada Lovelace");
        main.Status.ShouldBeNull();
        main.Problem.ShouldBeNull();
        host.Services.GetRequiredService<DesktopSession>().IsSignedIn.ShouldBeTrue();
    }

    [Fact]
    public async Task ASignInTheProviderRefuses_ShowsItsError()
    {
        var provider = new StubProvider();
        var browser = new ScriptedBrowser(state => $"error=access_denied&error_description=Denied&state={state}");
        using var host = PresentationHost.Build(new StubApi(request => provider.Answer(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound)), browser: browser);
        var main = host.Services.GetRequiredService<MainViewModel>();

        await main.SignInCommand.ExecuteAsync(Cancellation);

        main.Problem.ShouldBe("access_denied: Denied");
        main.SignedInAs.ShouldBeNull();
        main.Status.ShouldBeNull();
    }

    [Fact]
    public async Task SigningOut_ForgetsTheSession()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", "r1"));
        using var host = PresentationHost.Build(new StubApi(request => provider.Answer(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound)));
        var main = host.Services.GetRequiredService<MainViewModel>();
        await main.SignInCommand.ExecuteAsync(Cancellation);

        main.SignOutCommand.Execute(null);

        main.SignedInAs.ShouldBeNull();
        host.Services.GetRequiredService<DesktopSession>().IsSignedIn.ShouldBeFalse();
    }
    //#endif
}
