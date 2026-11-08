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

public sealed class AccountAdministrationServiceRoleTests
{
    private const string _role = "Admin";
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserStore<AppUser> _userStore =
        Substitute.For<IUserStore<AppUser>, IUserRoleStore<AppUser>, IUserSecurityStampStore<AppUser>>();

    private readonly IRoleStore<AppRole> _roleStore = Substitute.For<IRoleStore<AppRole>>();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddingARoleToAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var change = await CreateAdministration().GrantRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(AccountAdministrationStatus.NoSuchAccount);
    }

    [Fact]
    public async Task AddingAnUnseededRole_IsRejectedRatherThanThrowing()
    {
        var user = GivenTheAccountExists();
        GivenTheRoleIsSeeded(exists: false);

        var change = await CreateAdministration().GrantRoleAsync(_userId, "NoSuchRole", TestToken);

        change.Status.ShouldBe(AccountAdministrationStatus.Rejected);
        change.RejectionMessage.ShouldNotBeNull();
        user.SecurityStamp.ShouldNotBeNull();
    }

    [Fact]
    public async Task AddingASeededRole_Succeeds()
    {
        GivenTheAccountExists();
        GivenTheRoleIsSeeded(exists: true);

        var change = await CreateAdministration().GrantRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(AccountAdministrationStatus.Applied);
    }

    [Fact]
    public async Task AddingASeededRole_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        GivenTheRoleIsSeeded(exists: true);
        string stampBeforeGrant = user.SecurityStamp!;

        await CreateAdministration().GrantRoleAsync(_userId, _role, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBeforeGrant);
    }

    [Fact]
    public async Task RemovingARoleFromAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var change = await CreateAdministration().RevokeRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(AccountAdministrationStatus.NoSuchAccount);
    }

    [Fact]
    public async Task RemovingARoleTheAccountDoesNotCarry_IsRejectedRatherThanThrowing()
    {
        var user = GivenTheAccountExists();

        var change = await CreateAdministration().RevokeRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(AccountAdministrationStatus.Rejected);
        user.SecurityStamp.ShouldNotBeNull();
    }

    [Fact]
    public async Task RemovingARoleTheAccountCarries_Succeeds()
    {
        GivenTheAccountExists();
        GivenTheAccountCarries(_role);

        var change = await CreateAdministration().RevokeRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(AccountAdministrationStatus.Applied);
    }

    [Fact]
    public async Task RemovingARoleTheAccountCarries_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        GivenTheAccountCarries(_role);
        string stampBeforeRevocation = user.SecurityStamp!;

        await CreateAdministration().RevokeRoleAsync(_userId, _role, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBeforeRevocation);
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

        _userStore.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);

        var roleStore = (IUserRoleStore<AppUser>)_userStore;

        roleStore.IsInRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(_carriedRoles.Contains((string)callInfo[1]!)));
        roleStore.AddToRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _carriedRoles.Add((string)callInfo[1]!);
                return Task.CompletedTask;
            });
        roleStore.RemoveFromRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _carriedRoles.Remove((string)callInfo[1]!);
                return Task.CompletedTask;
            });

        var securityStampStore = (IUserSecurityStampStore<AppUser>)_userStore;
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

    private readonly HashSet<string> _carriedRoles = [];

    private void GivenTheAccountCarries(string role) => _carriedRoles.Add(role.ToUpperInvariant());

    private void GivenTheRoleIsSeeded(bool exists) =>
        _roleStore.FindByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(exists ? new AppRole(_role) { NormalizedName = _role.ToUpperInvariant() } : null);

    private AccountAdministrationService CreateAdministration()
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());
        var lookupNormalizer = new UpperInvariantLookupNormalizer();

        var userManager = new UserManager<AppUser>(
            _userStore,
            options,
            Substitute.For<IPasswordHasher<AppUser>>(),
            [],
            [],
            lookupNormalizer,
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        var roleManager = new RoleManager<AppRole>(
            _roleStore,
            [],
            lookupNormalizer,
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<AppRole>>.Instance);

        return new AccountAdministrationService(userManager, roleManager, _directory);
    }
}
