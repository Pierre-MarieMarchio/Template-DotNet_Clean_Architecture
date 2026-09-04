using AppTemplate.Application.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Services;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Services;

/// <summary>
/// <see cref="RoleAssignmentsService"/> over a real <see cref="UserManager{TUser}"/> and
/// <see cref="RoleManager{TRole}"/>: the gap this type exists to close is that neither
/// <c>AddToRoleAsync</c> nor <c>RemoveFromRoleAsync</c> rotates the security stamp on its own.
/// </summary>
public sealed class RoleAssignmentsServiceTests
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

        var change = await CreateRoleAssignments().AddRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(RoleAssignmentChangeStatus.NoSuchAccount);
    }

    [Fact]
    public async Task AddingAnUnseededRole_IsRejectedRatherThanThrowing()
    {
        var user = GivenTheAccountExists();
        GivenTheRoleIsSeeded(exists: false);

        var change = await CreateRoleAssignments().AddRoleAsync(_userId, "NoSuchRole", TestToken);

        change.Status.ShouldBe(RoleAssignmentChangeStatus.Rejected);
        user.SecurityStamp.ShouldNotBeNull();
    }

    [Fact]
    public async Task AddingASeededRole_Succeeds()
    {
        GivenTheAccountExists();
        GivenTheRoleIsSeeded(exists: true);

        var change = await CreateRoleAssignments().AddRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(RoleAssignmentChangeStatus.Applied);
    }

    /// <summary>
    /// The gap this port exists to close: without this, a role granted just now has no effect on the
    /// account's access until its current access token expires and it signs in again.
    /// </summary>
    [Fact]
    public async Task AddingASeededRole_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        GivenTheRoleIsSeeded(exists: true);
        string stampBeforeGrant = user.SecurityStamp!;

        await CreateRoleAssignments().AddRoleAsync(_userId, _role, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBeforeGrant);
    }

    [Fact]
    public async Task RemovingARoleFromAnUnknownAccount_IsReportedAsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var change = await CreateRoleAssignments().RemoveRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(RoleAssignmentChangeStatus.NoSuchAccount);
    }

    [Fact]
    public async Task RemovingARoleTheAccountDoesNotCarry_IsRejectedRatherThanThrowing()
    {
        var user = GivenTheAccountExists();

        var change = await CreateRoleAssignments().RemoveRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(RoleAssignmentChangeStatus.Rejected);
        user.SecurityStamp.ShouldNotBeNull();
    }

    [Fact]
    public async Task RemovingARoleTheAccountCarries_Succeeds()
    {
        GivenTheAccountExists();
        GivenTheAccountCarries(_role);

        var change = await CreateRoleAssignments().RemoveRoleAsync(_userId, _role, TestToken);

        change.Status.ShouldBe(RoleAssignmentChangeStatus.Applied);
    }

    /// <summary>
    /// The gap this port exists to close, in the other direction: without this, a role just revoked
    /// keeps authorising whatever it granted for as long as the account's current access token
    /// remains valid.
    /// </summary>
    [Fact]
    public async Task RemovingARoleTheAccountCarries_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        GivenTheAccountCarries(_role);
        string stampBeforeRevocation = user.SecurityStamp!;

        await CreateRoleAssignments().RemoveRoleAsync(_userId, _role, TestToken);

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

        // NSubstitute defaults an unconfigured Task<IdentityResult> member to a null result, and
        // UserManager reads .Succeeded off it without a null check.
        _userStore.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);

        var roleStore = (IUserRoleStore<AppUser>)_userStore;

        // Backed by a set on the user rather than a fixed answer, so IsInRoleAsync reflects whatever
        // GivenTheAccountCarries set up and AddToRoleAsync/RemoveFromRoleAsync actually change it.
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

    /// <summary>
    /// Stored upper-invariant: <see cref="UserManager{TUser}"/> normalises a role name before it ever
    /// reaches the store, through the same <see cref="UpperInvariantLookupNormalizer"/>
    /// <see cref="CreateRoleAssignments"/> configures, so seeding the raw, mixed-case name here would
    /// never match what <c>IsInRoleAsync</c> is actually asked about.
    /// </summary>
    private void GivenTheAccountCarries(string role) => _carriedRoles.Add(role.ToUpperInvariant());

    private void GivenTheRoleIsSeeded(bool exists) =>
        _roleStore.FindByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(exists ? new AppRole(_role) { NormalizedName = _role.ToUpperInvariant() } : null);

    private RoleAssignmentsService CreateRoleAssignments()
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

        return new RoleAssignmentsService(userManager, roleManager, _directory);
    }
}
