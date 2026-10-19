using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

/// <summary>
/// <see cref="AccountsService.FindProfileAsync"/> always reads the store — see <c>IAccountsService</c> for why a claims-based
/// shortcut is refused — so these assert what it reads and how it maps what comes back.
/// </summary>
public sealed class AccountsServiceProfileTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>, IUserRoleStore<AppUser>>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnknownId_ReturnsNull()
    {
        _store.FindByIdAsync(_userId.ToString("D"), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var profile = await CreateAccounts().FindProfileAsync(_userId, TestToken);

        profile.ShouldBeNull();
    }

    [Fact]
    public async Task AKnownId_ReturnsEveryField()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            Id = _userId,
            UserName = "someone",
            Email = "someone@example.com",
            EmailConfirmed = true,
            TwoFactorEnabled = true,
            CreatedAt = createdAt,
        };

        _store.FindByIdAsync(_userId.ToString("D"), Arg.Any<CancellationToken>()).Returns(user);
        ((IUserRoleStore<AppUser>)_store).GetRolesAsync(user, Arg.Any<CancellationToken>()).Returns(["Administrator"]);

        var profile = await CreateAccounts().FindProfileAsync(_userId, TestToken);

        profile.ShouldNotBeNull();
        profile.UserId.ShouldBe(_userId);
        profile.UserName.ShouldBe("someone");
        profile.Email.ShouldBe("someone@example.com");
        profile.EmailConfirmed.ShouldBeTrue();
        profile.Roles.ShouldBe(["Administrator"]);
        profile.CreatedAt.ShouldBe(createdAt);
        profile.TwoFactorEnabled.ShouldBeTrue();
    }

    private AccountsService CreateAccounts()
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

        var signInManager = Substitute.For<SignInManager<AppUser>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<AppUser>>(),
            options,
            NullLogger<SignInManager<AppUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<AppUser>>());

        return new AccountsService(
            userManager,
            signInManager,
            Substitute.For<IAppUserDirectory>(),
            Substitute.For<IDateTimeProvider>());
    }
}
