using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Services;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Services;

/// <summary>
/// <see cref="UserAccountsService.ChangePasswordAsync"/> over a real <see cref="UserManager{TUser}"/>: which
/// of ASP.NET Identity's refusals is reported as what.
/// </summary>
public sealed class UserAccountsServiceChangePasswordTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();
    private const string _currentPassword = "correct horse battery";
    private const string _storedHash = "stored-hash";

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>, IUserPasswordStore<AppUser>>();
    private readonly ConfigurableHasher _passwordHasher = new();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsReportedAsAnIncorrectCurrentPassword()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var change = await CreateAccounts().ChangePasswordAsync(_userId, _currentPassword, "a new password", TestToken);

        change.Status.ShouldBe(PasswordChangeStatus.IncorrectCurrentPassword);
    }

    [Fact]
    public async Task AWrongCurrentPassword_IsRefused()
    {
        GivenTheAccountExists();
        _passwordHasher.NextVerification = PasswordVerificationResult.Failed;

        var change = await CreateAccounts().ChangePasswordAsync(_userId, "wrong password", "a new password", TestToken);

        change.Status.ShouldBe(PasswordChangeStatus.IncorrectCurrentPassword);
    }

    [Fact]
    public async Task AMatchingCurrentPassword_Succeeds()
    {
        GivenTheAccountExists();
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        var change = await CreateAccounts().ChangePasswordAsync(_userId, _currentPassword, "a new password", TestToken);

        change.Status.ShouldBe(PasswordChangeStatus.Changed);
    }

    /// <summary>The verification derives a key exactly once, whatever it costs to hash for real.</summary>
    [Fact]
    public async Task AMatchingCurrentPassword_ReplacesTheStoredHash()
    {
        GivenTheAccountExists();
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        await CreateAccounts().ChangePasswordAsync(_userId, _currentPassword, "a new password", TestToken);

        await ((IUserPasswordStore<AppUser>)_store).Received(1)
            .SetPasswordHashAsync(Arg.Any<AppUser>(), Arg.Is<string>(hash => hash != _storedHash), Arg.Any<CancellationToken>());
    }

    private void GivenTheAccountExists()
    {
        var user = new AppUser { Id = _userId, UserName = "someone" };
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
        ((IUserPasswordStore<AppUser>)_store).GetPasswordHashAsync(user, Arg.Any<CancellationToken>()).Returns(_storedHash);
        ((IUserPasswordStore<AppUser>)_store).HasPasswordAsync(user, Arg.Any<CancellationToken>()).Returns(true);

        // NSubstitute defaults an unconfigured Task<IdentityResult> member to a null result, and
        // UserManager reads .Succeeded off it without a null check.
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);
    }

    private UserAccountsService CreateAccounts()
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());

        var userManager = new UserManager<AppUser>(
            _store,
            options,
            _passwordHasher,
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

        return new UserAccountsService(userManager, signInManager, _directory, Substitute.For<IDateTimeProvider>(), Substitute.For<ISecurityEventLog>());
    }

    /// <summary>A hasher whose verification result the test controls, unlike the fixed decoy in <c>RecordingPasswordHasher</c>.</summary>
    private sealed class ConfigurableHasher : IPasswordHasher<AppUser>
    {
        public PasswordVerificationResult NextVerification { get; set; } = PasswordVerificationResult.Failed;

        public string HashPassword(AppUser user, string password) => $"hash-of-{password}";

        public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword) =>
            NextVerification;
    }
}
