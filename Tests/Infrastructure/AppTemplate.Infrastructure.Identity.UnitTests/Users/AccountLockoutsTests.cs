using AppTemplate.Application.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Infrastructure.Identity.Users;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Users;

/// <summary>
/// <see cref="AccountLockouts"/> over a real <see cref="UserManager{TUser}"/>: whether locking and
/// unlocking reach the store the way the automatic, timed lockout in
/// <c>UserAccounts.VerifyCredentialAsync</c> already does, and — the gap this type exists to close —
/// whether locking also rotates the security stamp.
/// </summary>
public sealed class AccountLockoutsTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserStore<AppUser> _store =
        Substitute.For<IUserStore<AppUser>, IUserLockoutStore<AppUser>, IUserSecurityStampStore<AppUser>>();

    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LockingAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var outcome = await CreateLockouts().LockAsync(_userId, TestToken);

        outcome.ShouldBe(LockoutChangeOutcome.NoSuchAccount);
    }

    [Fact]
    public async Task LockingAKnownAccount_SetsAnIndefiniteLockoutEnd()
    {
        var user = GivenTheAccountExists();

        var outcome = await CreateLockouts().LockAsync(_userId, TestToken);

        outcome.ShouldBe(LockoutChangeOutcome.Applied);
        user.LockoutEnabled.ShouldBeTrue();
        user.LockoutEnd.ShouldBe(DateTimeOffset.MaxValue);
    }

    /// <summary>
    /// The gap this port exists to close: without this, an access token issued just before the lock
    /// keeps validating for as long as it has left to live.
    /// </summary>
    [Fact]
    public async Task LockingAKnownAccount_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        string stampBeforeLock = user.SecurityStamp!;

        await CreateLockouts().LockAsync(_userId, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBeforeLock);
    }

    [Fact]
    public async Task UnlockingAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var outcome = await CreateLockouts().UnlockAsync(_userId, TestToken);

        outcome.ShouldBe(LockoutChangeOutcome.NoSuchAccount);
    }

    [Fact]
    public async Task UnlockingAKnownAccount_ClearsTheLockoutEnd()
    {
        var user = GivenTheAccountExists();
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;

        var outcome = await CreateLockouts().UnlockAsync(_userId, TestToken);

        outcome.ShouldBe(LockoutChangeOutcome.Applied);
        user.LockoutEnd.ShouldBeNull();
    }

    /// <summary>
    /// <c>SetLockoutEndDateAsync</c> refuses outright — <c>UserLockoutNotEnabled</c> — when the
    /// account's <c>LockoutEnabled</c> flag is false, which is also exactly the state an account is
    /// in when nobody has ever locked it out. Discovered by this suite: without the fallback in
    /// <see cref="AccountLockouts.UnlockAsync"/>, this case answered <c>Rejected</c> for an account
    /// that was never anything but unlocked.
    /// </summary>
    [Fact]
    public async Task UnlockingAnAccountThatWasNeverLockedOut_IsStillReportedAsApplied()
    {
        GivenTheAccountExists();

        var outcome = await CreateLockouts().UnlockAsync(_userId, TestToken);

        outcome.ShouldBe(LockoutChangeOutcome.Applied);
    }

    /// <summary>
    /// Unlike locking, lifting a lockout grants access back rather than taking it away: there is no
    /// live credential of the target's for it to invalidate.
    /// </summary>
    [Fact]
    public async Task UnlockingAKnownAccount_DoesNotRotateTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        string stampBeforeUnlock = user.SecurityStamp!;

        await CreateLockouts().UnlockAsync(_userId, TestToken);

        user.SecurityStamp.ShouldBe(stampBeforeUnlock);
    }

    private AppUser GivenTheAccountExists()
    {
        var user = new AppUser
        {
            Id = _userId,
            UserName = "someone",
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
        };

        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);

        // NSubstitute defaults an unconfigured Task<IdentityResult> member to a null result, and
        // UserManager reads .Succeeded off it without a null check.
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);

        var lockoutStore = (IUserLockoutStore<AppUser>)_store;
        lockoutStore.GetLockoutEnabledAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((AppUser)callInfo[0]!).LockoutEnabled));
        lockoutStore.SetLockoutEnabledAsync(Arg.Any<AppUser>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                ((AppUser)callInfo[0]!).LockoutEnabled = (bool)callInfo[1]!;
                return Task.CompletedTask;
            });
        lockoutStore.GetLockoutEndDateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((AppUser)callInfo[0]!).LockoutEnd));
        lockoutStore.SetLockoutEndDateAsync(Arg.Any<AppUser>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                ((AppUser)callInfo[0]!).LockoutEnd = (DateTimeOffset?)callInfo[1];
                return Task.CompletedTask;
            });

        var securityStampStore = (IUserSecurityStampStore<AppUser>)_store;
        securityStampStore.GetSecurityStampAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((AppUser)callInfo[0]!).SecurityStamp));
        securityStampStore.SetSecurityStampAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                ((AppUser)callInfo[0]!).SecurityStamp = (string)callInfo[1]!;
                return Task.CompletedTask;
            });

        return user;
    }

    private AccountLockouts CreateLockouts()
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());

        var userManager = new UserManager<AppUser>(
            _store,
            options,
            Substitute.For<IPasswordHasher<AppUser>>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        return new AccountLockouts(userManager, _directory);
    }
}
