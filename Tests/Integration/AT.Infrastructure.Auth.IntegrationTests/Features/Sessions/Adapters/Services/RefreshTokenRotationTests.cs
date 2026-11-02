using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;
using AppTemplate.Infrastructure.Core.Common.Saving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Features.Sessions.Adapters.Services;

public sealed class RefreshTokenRotationTests(GrantTableFixture fixture) : IClassFixture<GrantTableFixture>
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoSimultaneousPresentationsOfTheSameToken_RotateItExactlyOnce()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(user.Id);

        var rendezvous = new Rendezvous(participants: 2);

        var rotations = await Task.WhenAll(
            PresentAsync(user, issued.Value, rendezvous),
            PresentAsync(user, issued.Value, rendezvous));

        rotations.Count(rotation => rotation.Succeeded).ShouldBe(
            1,
            "a refresh token is single-use: of two simultaneous presentations exactly one may be " +
            "issued a successor. Two winners means two live chains from one stolen token.");

        var refused = rotations.Single(rotation => !rotation.Succeeded);
        refused.Token.ShouldBeNull();
        refused.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task PresentingAConsumedToken_FailsAndRevokesTheWholeFamily()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var first = await IssueAsync(user.Id);

        var rotated = await PresentAsync(user, first.Value);
        rotated.Succeeded.ShouldBeTrue();

        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(1);

        var replayed = await PresentAsync(user, first.Value);

        replayed.Succeeded.ShouldBeFalse();
        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(
            0,
            "presenting a consumed token is either a replay or a leak, so every grant for that " +
            "account goes — otherwise whoever stole it keeps the successor they were issued.");
    }

    [Fact]
    public async Task AnUnknownToken_IsRejectedAndTouchesNothing()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        await IssueAsync(user.Id);

        var rotation = await PresentAsync(user, "a-token-nobody-ever-issued");

        rotation.Succeeded.ShouldBeFalse();
        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(1);
    }

    [Fact]
    public async Task ARotation_KeepsThePresentedTokensSession()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(user.Id);

        var rotated = await PresentAsync(user, issued.Value);

        rotated.Succeeded.ShouldBeTrue();
        rotated.Token!.SessionId.ShouldBe(issued.SessionId);
    }

    [Fact]
    public async Task ATokensOfAnEndedSession_AreRefusedWithoutTouchingTheOtherSessions()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var ended = await IssueAsync(user.Id);
        var spared = await IssueAsync(user.Id);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await CreateGrants(scope.ServiceProvider, Substitute.For<IAppUserDirectory>())
                .RevokeRefreshTokenAsync(ended.Value, TestToken);
        }

        var presented = await PresentAsync(user, ended.Value);

        presented.Succeeded.ShouldBeFalse();
        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(
            1,
            "ending one session must not be answered as a replay, which would end every session of " +
            "the account — the spared one included.");
        (await fixture.CountLiveSessionsAsync(user.Id, TestToken)).ShouldBe(1);
    }

    [Fact]
    public async Task EndingEverySessionButOne_LeavesThatOnesGrantLive()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var spared = await IssueAsync(user.Id);
        await IssueAsync(user.Id);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await CreateGrants(scope.ServiceProvider, Substitute.For<IAppUserDirectory>())
                .RevokeAllRefreshTokensAsync(user.Id, spared.SessionId, TestToken);
        }

        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(1);
        (await fixture.CountLiveSessionsAsync(user.Id, TestToken)).ShouldBe(
            1,
            "the session the sweep spares must still be usable, which a live grant alone does not " +
            "prove: a revoked family serves none of its grants.");
    }

    [Fact]
    public async Task ASaveThatFailsAfterTheConsumption_LeavesThePresentedTokenRotatable()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(user.Id);
        var log = Substitute.For<ISecurityEventLog>();
        var fault = new OneTimeFault(FaultPoint.BeforeTheSuccessorsInsert, transient: false);

        await using (var failing = fixture.ComposeWith(fault.Interceptors))
        {
            await Should.ThrowAsync<Exception>(() => PresentThroughAsync(failing, user, issued.Value, log));
        }

        fault.Fired.ShouldBeTrue();

        var retried = await PresentThroughAsync(fixture.Services, user, issued.Value, log);

        retried.Succeeded.ShouldBeTrue(
            "the failed attempt issued no successor, so it must not have consumed the grant either.");
        log.DidNotReceiveWithAnyArgs().Record(default!);
        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(1);
    }

    [Theory]
    [InlineData(FaultPoint.AfterTheConsumption)]
    [InlineData(FaultPoint.BeforeTheSuccessorsInsert)]
    [InlineData(FaultPoint.AfterTheCommit)]
    public async Task ATransientFailureDuringARotation_IsRetriedWithoutAFalseReplay(FaultPoint point)
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(user.Id);
        var log = Substitute.For<ISecurityEventLog>();
        var fault = new OneTimeFault(point, transient: true);

        RefreshTokenRotation rotation;

        await using (var failing = fixture.ComposeWith(fault.Interceptors))
        {
            rotation = await PresentThroughAsync(failing, user, issued.Value, log);
        }

        fault.Fired.ShouldBeTrue();
        rotation.Succeeded.ShouldBeTrue();
        log.DidNotReceiveWithAnyArgs().Record(default!);
        (await fixture.CountLiveGrantsAsync(user.Id, TestToken)).ShouldBe(
            1,
            "exactly one successor: the retry neither lost the one it staged nor inserted it twice.");
    }

    private async Task<IssuedRefreshToken> IssueAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        return await CreateGrants(scope.ServiceProvider, Substitute.For<IAppUserDirectory>())
            .IssueRefreshTokenAsync(userId, SessionClient.Unknown, TestToken);
    }

    private async Task<RefreshTokenRotation> PresentAsync(
        AppUser user,
        string presentedToken,
        Rendezvous? rendezvous = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        var directory = Substitute.For<IAppUserDirectory>();

        directory.FindByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(_ => ArriveThenReturnAsync(rendezvous, user));

        return await CreateGrants(scope.ServiceProvider, directory)
            .RotateRefreshTokenAsync(presentedToken, SessionClient.Unknown, TestToken);
    }

    private static async Task<RefreshTokenRotation> PresentThroughAsync(
        IServiceProvider services,
        AppUser user,
        string presentedToken,
        ISecurityEventLog log)
    {
        await using var scope = services.CreateAsyncScope();

        var directory = Substitute.For<IAppUserDirectory>();
        directory.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        return await CreateGrants(scope.ServiceProvider, directory, log)
            .RotateRefreshTokenAsync(presentedToken, SessionClient.Unknown, TestToken);
    }

    private static async Task<AppUser?> ArriveThenReturnAsync(Rendezvous? rendezvous, AppUser user)
    {
        if (rendezvous is not null)
        {
            await rendezvous.ArriveAsync(TestToken);
        }

        return user;
    }

    private static SessionTokensService CreateGrants(
        IServiceProvider scoped,
        IAppUserDirectory directory,
        ISecurityEventLog? log = null) =>
        new(
            new AccessTokenIssuer(
                directory,
                new OptionsWrapper<JwtOptions>(new JwtOptions()),
                scoped.GetRequiredService<IDateTimeProvider>()),
            scoped.GetRequiredService<IRefreshTokenTable>(),
            scoped.GetRequiredService<IRefreshTokenFamilyTable>(),
            directory,
            scoped.GetRequiredService<IContextUnitOfWork<AuthDbContext>>(),
            scoped.GetRequiredService<AuthDbContext>(),
            scoped.GetRequiredService<IDateTimeProvider>(),
            new OptionsWrapper<RefreshTokenOptions>(new RefreshTokenOptions()),
            log ?? Substitute.For<ISecurityEventLog>(),
            NullLogger<SessionTokensService>.Instance);
}
