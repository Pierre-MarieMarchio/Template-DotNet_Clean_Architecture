using System.ComponentModel;
using System.Net;
using AppTemplate.PresentationApp.Core.Common.Sessions;
using Duende.IdentityModel.OidcClient.Browser;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.Core.UnitTests.Common.Sessions;

public sealed class LoopbackBrowserTests
{
    private const string _authorize = "https://login.test/authorize?client_id=desktop-app";

    private static readonly HttpClient _http = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void TheRedirect_IsOnTheLoopbackAddress_WithAPortOfItsOwn()
    {
        using var first = new LoopbackBrowser(new RecordingBrowser());
        using var second = new LoopbackBrowser(new RecordingBrowser());

        first.RedirectUri.Scheme.ShouldBe("http");
        first.RedirectUri.Host.ShouldBe("127.0.0.1");
        first.RedirectUri.Port.ShouldNotBe(second.RedirectUri.Port);
    }

    [Fact]
    public async Task TheCallback_OpensTheBrowserOnTheAuthorizeUrl_AndReturnsItsQuery()
    {
        var opened = new RecordingBrowser();
        using var browser = new LoopbackBrowser(opened);
        var signIn = browser.InvokeAsync(Options(browser, TimeSpan.FromSeconds(30)), Cancellation);

        string page = await _http.GetStringAsync(new Uri(browser.RedirectUri, "?code=c1&state=s1"), Cancellation);
        var result = await signIn;

        opened.Address.ShouldBe(new Uri(_authorize));
        result.ResultType.ShouldBe(BrowserResultType.Success);
        result.Response.ShouldBe("?code=c1&state=s1");
        page.ShouldContain(LoopbackBrowser.ReturnToTheApp);
    }

    [Fact]
    public async Task ARequestThatIsNotTheCallback_IsAnswered404_AndTheCallbackStillAwaited()
    {
        using var browser = new LoopbackBrowser(new RecordingBrowser());
        var signIn = browser.InvokeAsync(Options(browser, TimeSpan.FromSeconds(30)), Cancellation);

        using var favicon = await _http.GetAsync(new Uri(browser.RedirectUri, "/favicon.ico"), Cancellation);
        using var bare = await _http.GetAsync(browser.RedirectUri, Cancellation);
        using var callback = await _http.GetAsync(new Uri(browser.RedirectUri, "?error=access_denied&state=s1"), Cancellation);

        favicon.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bare.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        callback.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await signIn).Response.ShouldBe("?error=access_denied&state=s1");
    }

    [Fact]
    public async Task NoCallbackInTime_EndsTheSignIn_AsATimeout()
    {
        using var browser = new LoopbackBrowser(new RecordingBrowser());

        var result = await browser.InvokeAsync(Options(browser, TimeSpan.FromMilliseconds(200)), Cancellation)
            .WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        result.ResultType.ShouldBe(BrowserResultType.Timeout);
        result.Error.ShouldBe(LoopbackBrowser.TimedOut);
    }

    [Fact]
    public async Task ACancelledSignIn_StopsWaiting()
    {
        using var browser = new LoopbackBrowser(new RecordingBrowser());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var signIn = browser.InvokeAsync(Options(browser, TimeSpan.FromSeconds(30)), cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => signIn);
    }

    [Fact]
    public async Task NoBrowserToOpen_EndsTheSignIn_WithoutWaiting()
    {
        using var browser = new LoopbackBrowser(new RecordingBrowser(new Win32Exception("No application is associated with the address.")));

        var result = await browser.InvokeAsync(Options(browser, TimeSpan.FromSeconds(30)), Cancellation);

        result.ResultType.ShouldBe(BrowserResultType.UnknownError);
        result.Error.ShouldBe(LoopbackBrowser.NoBrowser);
    }

    private static BrowserOptions Options(LoopbackBrowser browser, TimeSpan timeout) =>
        new(_authorize, browser.RedirectUri.AbsoluteUri) { Timeout = timeout };

    private sealed class RecordingBrowser(Exception? failure = null) : ISystemBrowser
    {
        public Uri? Address { get; private set; }

        public void Open(Uri address)
        {
            Address = address;

            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
