using AppTemplate.Application.Features.Auth.Ports.AccountDeletion;
using AppTemplate.Infrastructure.Identity.Accounts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Accounts;

/// <summary>
/// <see cref="AccountDeletionService"/> over a real <see cref="UserManager{TUser}"/>. Unlike
/// <see cref="AccountLockoutsServiceTests"/> and <see cref="RoleAssignmentsServiceTests"/>, there is no security
/// stamp to assert on: see <see cref="AccountDeletionService"/> for why deleting the row needs none.
/// </summary>
public sealed class AccountDeletionServiceTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserStore<AppUser> _store = Substitute.For<IUserStore<AppUser>>();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeletingAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var outcome = await CreateAccountDeletion().DeleteAsync(_userId, TestToken);

        outcome.ShouldBe(AccountDeletionStatus.NoSuchAccount);
    }

    [Fact]
    public async Task DeletingAKnownAccount_RemovesItFromTheStore()
    {
        var user = GivenTheAccountExists();

        var outcome = await CreateAccountDeletion().DeleteAsync(_userId, TestToken);

        outcome.ShouldBe(AccountDeletionStatus.Deleted);
        await _store.Received(1).DeleteAsync(user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreThatRefusesTheDelete_IsReportedAsRejected()
    {
        GivenTheAccountExists();
        _store.DeleteAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));

        var outcome = await CreateAccountDeletion().DeleteAsync(_userId, TestToken);

        outcome.ShouldBe(AccountDeletionStatus.Rejected);
    }

    private AppUser GivenTheAccountExists()
    {
        var user = new AppUser { Id = _userId, UserName = "someone" };

        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
        _store.DeleteAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        return user;
    }

    private AccountDeletionService CreateAccountDeletion()
    {
        var userManager = new UserManager<AppUser>(
            _store,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            Substitute.For<IPasswordHasher<AppUser>>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        return new AccountDeletionService(userManager, _directory);
    }
}
