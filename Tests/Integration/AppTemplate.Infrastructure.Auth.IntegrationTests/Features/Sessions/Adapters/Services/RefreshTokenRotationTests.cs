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

/// <summary>
/// Single-use rotation, against a real database.
/// </summary>
/// <remarks>
/// This is the one guarantee the whole refresh-token design rests on: a grant can be redeemed once,
/// and a second redemption is evidence that the chain leaked. It is a guarantee about what happens
/// when two requests arrive at the same instant, so it is asserted the same way — two presentations
/// held at a rendezvous until both are past the read, then released into the write together.
/// </remarks>
public sealed class RefreshTokenRotationTests(GrantTableFixture fixture) : IClassFixture<GrantTableFixture>
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoSimultaneousPresentationsOfTheSameToken_RotateItExactlyOnce()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(user.Id);

        // Both presentations are released from the rendezvous after each has read the grant and found
        // it live, which is the one window in which both could rotate it.
        var rendezvous = new Rendezvous(participants: 2);

        var rotations = await Task.WhenAll(
            PresentAsync(user, issued.Value, rendezvous),
            PresentAsync(user, issued.Value, rendezvous));

        rotations.Count(rotation => rotation.Succeeded).ShouldBe(
            1,
            "a refresh token is single-use: of two simultaneous presentations exactly one may be " +
            "issued a successor. Two winners means two live chains from one stolen token.");

        // The loser is handed nothing at all — not a successor, not an account to mint claims for.
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

        // A live grant exists at this point — the successor — so anything that stops working below is
        // the family revocation and not a chain that was already dead.
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

    /// <summary>
    /// One sign-in is one session: the successor of a rotation stays in the family the presented
    /// token started, so the session the access token names survives every refresh.
    /// </summary>
    [Fact]
    public async Task ARotation_KeepsThePresentedTokensSession()
    {
        var user = await fixture.CreateUserAsync(TestToken);
        var issued = await IssueAsync(user.Id);

        var rotated = await PresentAsync(user, issued.Value);

        rotated.Succeeded.ShouldBeTrue();
        rotated.Token!.SessionId.ShouldBe(issued.SessionId);
    }

    /// <summary>
    /// A token of a session that was ended is what a client still holding it sends next, so it is
    /// refused without the replay alarm: nothing else of the account's goes, and in particular the
    /// account's other sessions do not.
    /// </summary>
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

    /// <summary>
    /// The exception a credential change asks for: every session of the account ends but the one the
    /// change was made from, whose live grant — and only whose — must survive the sweep.
    /// </summary>
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

    /// <summary>
    /// A rotation is one transaction: when anything after the consumption fails, the consumption is
    /// undone with it, so the client presenting the same token again is served rather than taken for
    /// a thief — which would end every session of the account and record a replay that never was.
    /// </summary>
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

    /// <summary>
    /// The identity context retries a transient failure, and a retry replays the whole rotation: an
    /// acknowledgement lost after the conditional <c>UPDATE</c> ran must not leave the replay to find
    /// its own consumption and answer it as theft, and a commit that landed but was not acknowledged
    /// must be recognised rather than replayed.
    /// </summary>
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

    /// <summary>
    /// One presentation of a token, in a scope of its own — one context, one unit of work — which is
    /// what a request gets.
    /// </summary>
    private async Task<RefreshTokenRotation> PresentAsync(
        AppUser user,
        string presentedToken,
        Rendezvous? rendezvous = null)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        // A substitute per presentation: the two run concurrently, and NSubstitute records calls on
        // an instance without synchronising them.
        var directory = Substitute.For<IAppUserDirectory>();

        // The rotation reads the grant, then looks the account up, then writes. Holding the callers
        // here puts every one of them past the read and short of the write at the same moment.
        directory.FindByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(_ => ArriveThenReturnAsync(rendezvous, user));

        return await CreateGrants(scope.ServiceProvider, directory)
            .RotateRefreshTokenAsync(presentedToken, SessionClient.Unknown, TestToken);
    }

    /// <summary>One presentation through <paramref name="services"/>, recording to <paramref name="log"/>.</summary>
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

    /// <summary>
    /// The adapter as the container composes it, except for the account lookup: a real
    /// <c>UserManager</c> would drag ASP.NET Identity's whole store in for one <c>FindById</c>, and
    /// that lookup is not what these tests are about.
    /// </summary>
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
