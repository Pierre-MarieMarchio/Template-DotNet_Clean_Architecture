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

public sealed class AccountAdministrationServiceDeletionTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserStore<AppUser> _store = Substitute.For<IUserStore<AppUser>>();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeletingAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var outcome = await CreateAdministration().DeleteAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.NoSuchAccount);
    }

    [Fact]
    public async Task DeletingAKnownAccount_RemovesItFromTheStore()
    {
        var user = GivenTheAccountExists();

        var outcome = await CreateAdministration().DeleteAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.Applied);
        await _store.Received(1).DeleteAsync(user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreThatRefusesTheDelete_IsReportedAsRejected()
    {
        GivenTheAccountExists();
        _store.DeleteAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));

        var outcome = await CreateAdministration().DeleteAsync(_userId, TestToken);

        outcome.Status.ShouldBe(AccountAdministrationStatus.Rejected);
    }

    private AppUser GivenTheAccountExists()
    {
        var user = new AppUser { Id = _userId, UserName = "someone" };

        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
        _store.DeleteAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        return user;
    }

    private AccountAdministrationService CreateAdministration()
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
