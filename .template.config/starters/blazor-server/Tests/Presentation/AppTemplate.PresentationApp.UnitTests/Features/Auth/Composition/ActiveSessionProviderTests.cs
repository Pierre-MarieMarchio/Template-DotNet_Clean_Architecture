using System.Globalization;
using System.Security.Claims;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.PresentationApp.Common.Security;
using AppTemplate.PresentationApp.Features.Auth.Composition;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Features.Auth.Composition;

public sealed class ActiveSessionProviderTests
{
    private static readonly Guid _session = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly IListSessionsUseCase _listSessions = Substitute.For<IListSessionsUseCase>();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private ClaimsPrincipal? _askedAs;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASessionTheAccountStillLists_IsActive()
    {
        Listing(_session, Guid.CreateVersion7());

        (await Sessions().IsActiveAsync(Principal(), TestToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task ASessionEndedElsewhere_IsNoLongerActive()
    {
        Listing(Guid.CreateVersion7());

        (await Sessions().IsActiveAsync(Principal(), TestToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task APrincipalNamingNoSession_IsNeverActive()
    {
        Listing(_session);

        (await Sessions().IsActiveAsync(new ClaimsPrincipal(new ClaimsIdentity()), TestToken)).ShouldBeFalse();
        await _listSessions.DidNotReceiveWithAnyArgs().ExecuteAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheListing_IsAskedAsTheCallerTheCookieNames()
    {
        Listing(_session);

        await Sessions().IsActiveAsync(Principal(), TestToken);

        _askedAs!.FindFirstValue("sid").ShouldBe(_session.ToString());
    }

    [Fact]
    public async Task ACookieCheckedWithinTheInterval_IsTrustedWithoutAskingAgain()
    {
        var context = Validating(checkedAt: _now - ActiveSessionProvider.RevalidationInterval / 2);

        await Sessions().ValidateCookieAsync(context);

        context.Principal.ShouldNotBeNull();
        await _listSessions.DidNotReceiveWithAnyArgs().ExecuteAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACookieDueForACheck_WhoseSessionLives_IsRenewedWithTheTimeOfTheCheck()
    {
        Listing(_session);
        var context = Validating(checkedAt: _now - ActiveSessionProvider.RevalidationInterval);

        await Sessions().ValidateCookieAsync(context);

        context.ShouldRenew.ShouldBeTrue();
        context.Properties.Items[ActiveSessionProvider.CheckedAtKey].ShouldBe(_now.ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ACookieWhoseSessionWasEnded_IsRejectedAndSignedOut()
    {
        Listing(Guid.CreateVersion7());
        var context = Validating(checkedAt: null);

        await Sessions().ValidateCookieAsync(context);

        context.Principal.ShouldBeNull();
        await _authentication.Received(1).SignOutAsync(
            context.HttpContext,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Any<AuthenticationProperties?>());
    }

    private ActiveSessionProvider Sessions()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(_now);

        var provider = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
            .AddScoped<CallerPrincipal>()
            .AddScoped<IListSessionsUseCase>(scope =>
            {
                _askedAs = scope.GetRequiredService<CallerPrincipal>().Principal;

                return _listSessions;
            })
            .BuildServiceProvider();

        return new ActiveSessionProvider(provider.GetRequiredService<IServiceScopeFactory>(), clock);
    }

    private CookieValidatePrincipalContext Validating(DateTimeOffset? checkedAt)
    {
        var properties = new AuthenticationProperties();

        if (checkedAt is { } at)
        {
            properties.Items[ActiveSessionProvider.CheckedAtKey] = at.ToString("O", CultureInfo.InvariantCulture);
        }

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(_authentication).BuildServiceProvider(),
        };

        return new CookieValidatePrincipalContext(
            http,
            new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(Principal(), properties, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private void Listing(params Guid[] sessions) =>
        _listSessions.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<ListSessionsItem>>(
            [.. sessions.Select(id => new ListSessionsItem(id, _now, _now, null, false))]));

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity([new Claim("sid", _session.ToString())], "test"));
}
