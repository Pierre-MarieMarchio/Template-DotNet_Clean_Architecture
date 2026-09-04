using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;
using AppTemplate.Infrastructure.Identity.Users;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Users;

/// <summary>
/// What the credential check costs, and which of ASP.NET Identity's refusals is reported as what.
/// </summary>
/// <remarks>
/// The cost matters as much as the answer. An unknown address that returns before doing any key
/// derivation answers in a fraction of the time a wrong password takes, and that difference is a
/// user-enumeration oracle no uniform error message hides.
/// </remarks>
public sealed class UserAccountsTests
{
    private const string _password = "correct horse battery";

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>>();
    private readonly RecordingPasswordHasher _passwordHasher = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Asserted through the hasher the container configured, not a fresh one, so the decoy and the
    /// real verification derive a key the same number of times whatever the iteration count is set
    /// to. Deleting the decoy leaves every uniformity test green and this one red.
    /// </summary>
    [Fact]
    public async Task AnUnknownAddress_StillCostsOnePasswordVerificationFromTheConfiguredHasher()
    {
        _store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var check = await CreateAccounts().VerifyCredentialAsync("nobody@identity.test", _password, TestToken);

        check.Outcome.ShouldBe(CredentialCheckOutcome.NoSuchAccount);
        check.Account.ShouldBeNull();

        _passwordHasher.Verifications.ShouldBe(
            1,
            "the unknown-address path derived no key, so it answers faster than a wrong password does " +
            "and the response time says which addresses are registered.");

        _passwordHasher.LastVerifiedHash.ShouldBe(RecordingPasswordHasher.Hash);
    }

    /// <summary>
    /// A refusal must not carry an account: a caller that got one could act on it whatever the
    /// outcome said.
    /// </summary>
    [Fact]
    public async Task AnUnknownAddress_NamesNoAccount()
    {
        _store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var check = await CreateAccounts().VerifyCredentialAsync("nobody@identity.test", _password, TestToken);

        check.Account.ShouldBeNull();
    }

    private UserAccounts CreateAccounts()
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

        // Never reached on the unknown-address path, which is what these tests are about, but the
        // constructor asks for it.
        var signInManager = Substitute.For<SignInManager<AppUser>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<AppUser>>(),
            options,
            NullLogger<SignInManager<AppUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<AppUser>>());

        return new UserAccounts(
            userManager,
            signInManager,
            Substitute.For<IAppUserDirectory>(),
            Substitute.For<IDateTimeProvider>());
    }
}
