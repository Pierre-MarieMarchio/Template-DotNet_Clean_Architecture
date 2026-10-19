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

/// <summary>
/// <see cref="AccountAdministrationService.DisableTwoFactorAsync"/> over a real <see cref="UserManager{TUser}"/>. It runs
/// the same disable sequence as <c>TwoFactorService.DisableAsync</c>, so these pin that it
/// still discards the secret and still reports a store refusal.
/// </summary>
public sealed class AccountAdministrationServiceTwoFactorTests
{
    private const string _oldKey = "OLDKEYOLDKEYOLDKEYOLDKEYOLDKEY23";

    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserStore<AppUser> _store = (IUserStore<AppUser>)Substitute.For(
        [
            typeof(IUserStore<AppUser>),
            typeof(IUserTwoFactorStore<AppUser>),
            typeof(IUserAuthenticatorKeyStore<AppUser>),
            typeof(IUserSecurityStampStore<AppUser>),
        ],
        []);

    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private string? _authenticatorKey = _oldKey;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DisableAsync_AnArmedAccount_TurnsTwoFactorOffAndDiscardsTheSecret()
    {
        var user = GivenAnArmedAccount();

        var outcome = await CreateAdministration().DisableTwoFactorAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.Applied);
        user.TwoFactorEnabled.ShouldBeFalse();
        _authenticatorKey.ShouldNotBe(_oldKey);
    }

    [Fact]
    public async Task DisableAsync_WhenTheStoreRefuses_ReportsRejectedAndKeepsTheSecret()
    {
        GivenAnArmedAccount();
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()));

        var outcome = await CreateAdministration().DisableTwoFactorAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.Rejected);
        _authenticatorKey.ShouldBe(_oldKey);
    }

    [Fact]
    public async Task DisableAsync_AnUnknownAccount_ReportsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var outcome = await CreateAdministration().DisableTwoFactorAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.NoSuchAccount);
    }

    private AppUser GivenAnArmedAccount()
    {
        var user = new AppUser
        {
            Id = _userId,
            UserName = "someone",
            Email = "someone@identity.test",
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
            TwoFactorEnabled = true,
        };

        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        var twoFactorStore = (IUserTwoFactorStore<AppUser>)_store;
        twoFactorStore.SetTwoFactorEnabledAsync(Arg.Any<AppUser>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                ((AppUser)callInfo[0]!).TwoFactorEnabled = (bool)callInfo[1]!;
                return Task.CompletedTask;
            });

        var authenticatorKeyStore = (IUserAuthenticatorKeyStore<AppUser>)_store;
        authenticatorKeyStore.SetAuthenticatorKeyAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _authenticatorKey = (string)callInfo[1]!;
                return Task.CompletedTask;
            });

        var securityStampStore = (IUserSecurityStampStore<AppUser>)_store;
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
        var userManager = new UserManager<AppUser>(
            _store,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<AppUser>(),
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
