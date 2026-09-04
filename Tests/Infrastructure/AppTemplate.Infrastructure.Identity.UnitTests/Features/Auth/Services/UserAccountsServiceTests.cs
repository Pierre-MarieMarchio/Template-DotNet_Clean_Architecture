using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Services;
using AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;
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
/// What the credential check costs, and which of ASP.NET Identity's refusals is reported as what.
/// </summary>
/// <remarks>
/// The cost matters as much as the answer. An unknown address that returns before doing any key
/// derivation answers in a fraction of the time a wrong password takes, and that difference is a
/// user-enumeration oracle no uniform error message hides.
/// </remarks>
public sealed class UserAccountsServiceTests : IDisposable
{
    private const string _password = "correct horse battery";

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>>();
    private readonly RecordingPasswordHasher _passwordHasher = new();
    private readonly OptionsWrapper<IdentityOptions> _options = new(new IdentityOptions());
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;

    public UserAccountsServiceTests()
    {
        _userManager = new UserManager<AppUser>(
            _store,
            _options,
            _passwordHasher,
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        _signInManager = Substitute.For<SignInManager<AppUser>>(
            _userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<AppUser>>(),
            _options,
            NullLogger<SignInManager<AppUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<AppUser>>());
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public void Dispose() => _userManager.Dispose();

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

        check.Status.ShouldBe(CredentialCheckStatus.NoSuchAccount);
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

    /// <summary>
    /// <see cref="SignInManager{TUser}.CheckPasswordSignInAsync"/> refuses a locked-out account
    /// before deriving a key at all, which would otherwise answer faster than a wrong password does.
    /// </summary>
    [Fact]
    public async Task ALockedOutAccount_StillCostsOnePasswordVerificationFromTheConfiguredHasher()
    {
        var user = GivenAKnownAccount();
        _signInManager.CheckPasswordSignInAsync(user, _password, true).Returns(SignInResult.LockedOut);

        var check = await CreateAccounts().VerifyCredentialAsync("known@identity.test", _password, TestToken);

        check.Status.ShouldBe(CredentialCheckStatus.LockedOut);
        _passwordHasher.Verifications.ShouldBe(
            1,
            "the lockout check answers before deriving a key, so a locked-out account must burn one " +
            "explicitly or it answers faster than a wrong password does.");
    }

    /// <summary>Same oracle, for the other refusal <c>PreSignInCheckAsync</c> can return early.</summary>
    [Fact]
    public async Task AnUnconfirmedEmailAccount_StillCostsOnePasswordVerificationFromTheConfiguredHasher()
    {
        var user = GivenAKnownAccount();
        _signInManager.CheckPasswordSignInAsync(user, _password, true).Returns(SignInResult.NotAllowed);

        var check = await CreateAccounts().VerifyCredentialAsync("known@identity.test", _password, TestToken);

        check.Status.ShouldBe(CredentialCheckStatus.EmailNotConfirmed);
        _passwordHasher.Verifications.ShouldBe(1);
    }

    /// <summary>
    /// A plain wrong password derives a key inside the mocked <c>CheckPasswordSignInAsync</c> call
    /// itself in production; this substitute never calls the real hasher, so the only thing worth
    /// asserting here is that this refusal does not also trigger the extra derivation added for
    /// <see cref="CredentialCheckStatus.LockedOut"/> and <see cref="CredentialCheckStatus.EmailNotConfirmed"/>.
    /// </summary>
    [Fact]
    public async Task AnIncorrectPassword_DoesNotTriggerTheExtraDerivation()
    {
        var user = GivenAKnownAccount();
        _signInManager.CheckPasswordSignInAsync(user, _password, true).Returns(SignInResult.Failed);

        var check = await CreateAccounts().VerifyCredentialAsync("known@identity.test", _password, TestToken);

        check.Status.ShouldBe(CredentialCheckStatus.IncorrectPassword);
        _passwordHasher.Verifications.ShouldBe(
            0,
            "CheckPasswordSignInAsync is the one that derives a key for a wrong password; a second " +
            "derivation here would cost twice what this refusal actually costs.");
    }

    private AppUser GivenAKnownAccount()
    {
        var user = new AppUser { Id = Guid.CreateVersion7(), UserName = "known", Email = "known@identity.test" };

        // UserManager.FindByEmailAsync normalizes before calling the store, so the substitute must
        // match on any address rather than the one the test passes in.
        _store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        return user;
    }

    private UserAccountsService CreateAccounts() =>
        new(
            _userManager,
            _signInManager,
            Substitute.For<IAppUserDirectory>(),
            Substitute.For<IDateTimeProvider>(),
            Substitute.For<ISecurityEventLog>());
}
