using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

public sealed class AccountAdministrationServiceLockoutTests
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

        var outcome = await CreateAdministration().LockAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.NoSuchAccount);
    }

    [Fact]
    public async Task LockingAKnownAccount_SetsAnIndefiniteLockoutEnd()
    {
        var user = GivenTheAccountExists();

        var outcome = await CreateAdministration().LockAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.Applied);
        user.LockoutEnabled.ShouldBeTrue();
        user.LockoutEnd.ShouldBe(DateTimeOffset.MaxValue);
    }

    [Fact]
    public async Task LockingAKnownAccount_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        string stampBeforeLock = user.SecurityStamp!;

        await CreateAdministration().LockAsync(_userId, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBeforeLock);
    }

    [Fact]
    public async Task UnlockingAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var outcome = await CreateAdministration().UnlockAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.NoSuchAccount);
    }

    [Fact]
    public async Task UnlockingAKnownAccount_ClearsTheLockoutEnd()
    {
        var user = GivenTheAccountExists();
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;

        var outcome = await CreateAdministration().UnlockAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.Applied);
        user.LockoutEnd.ShouldBeNull();
    }

    [Fact]
    public async Task UnlockingAnAccountThatWasNeverLockedOut_IsStillReportedAsApplied()
    {
        GivenTheAccountExists();

        var outcome = await CreateAdministration().UnlockAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.Applied);
    }

    [Fact]
    public async Task UnlockingAKnownAccount_DoesNotRotateTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        string stampBeforeUnlock = user.SecurityStamp!;

        await CreateAdministration().UnlockAsync(_userId, TestToken);

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

    private AccountAdministrationService CreateAdministration()
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

        return new AccountAdministrationService(
            userManager,
            new RoleManager<AppRole>(
                Substitute.For<IRoleStore<AppRole>>(),
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                NullLogger<RoleManager<AppRole>>.Instance),
            _directory);
    }
}
