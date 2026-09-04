using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using AppTemplate.Infrastructure.Identity.Features.Auth.Services;
using AppTemplate.Infrastructure.Identity.IntegrationTests.Fixtures;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.IntegrationTests.Features.Auth.Services;

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

    private async Task<IssuedRefreshToken> IssueAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        return await CreateGrants(scope.ServiceProvider, Substitute.For<IAppUserDirectory>())
            .IssueAsync(userId, TestToken);
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

        return await CreateGrants(scope.ServiceProvider, directory).RotateAsync(presentedToken, TestToken);
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
    private static RefreshTokenGrantsService CreateGrants(IServiceProvider scoped, IAppUserDirectory directory) =>
        new(
            scoped.GetRequiredService<IRefreshTokenTable>(),
            directory,
            scoped.GetRequiredService<IUnitOfWork>(),
            scoped.GetRequiredService<IDateTimeProvider>(),
            new OptionsWrapper<RefreshTokenOptions>(new RefreshTokenOptions()),
            Substitute.For<ISecurityEventLog>(),
            NullLogger<RefreshTokenGrantsService>.Instance);
}
