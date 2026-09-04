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
/// <see cref="UserProfiles"/> always reads the store — see <c>IUserProfiles</c> for why a claims-based
/// shortcut is refused — so these assert what it reads and how it maps what comes back.
/// </summary>
public sealed class UserProfilesTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>, IUserRoleStore<AppUser>>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnknownId_ReturnsNull()
    {
        _store.FindByIdAsync(_userId.ToString("D"), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var profile = await CreateProfiles().FindByIdAsync(_userId, TestToken);

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

        var profile = await CreateProfiles().FindByIdAsync(_userId, TestToken);

        profile.ShouldNotBeNull();
        profile.UserId.ShouldBe(_userId);
        profile.UserName.ShouldBe("someone");
        profile.Email.ShouldBe("someone@example.com");
        profile.EmailConfirmed.ShouldBeTrue();
        profile.Roles.ShouldBe(["Administrator"]);
        profile.CreatedAt.ShouldBe(createdAt);
        profile.TwoFactorEnabled.ShouldBeTrue();
    }

    private UserProfiles CreateProfiles()
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

        return new UserProfiles(userManager);
    }
}
