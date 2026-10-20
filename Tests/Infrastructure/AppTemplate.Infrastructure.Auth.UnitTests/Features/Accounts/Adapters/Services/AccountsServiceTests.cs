using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

public sealed class AccountsServiceTests : IDisposable
{
    private const string _password = "correct horse battery";

    private readonly IUserEmailStore<AppUser> _store = Substitute.For<IUserEmailStore<AppUser>>();
    private readonly RecordingPasswordHasher _passwordHasher = new();
    private readonly OptionsWrapper<IdentityOptions> _options = new(new IdentityOptions());
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;

    public AccountsServiceTests()
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

    [Fact]
    public async Task AnUnknownAddress_NamesNoAccount()
    {
        _store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var check = await CreateAccounts().VerifyCredentialAsync("nobody@identity.test", _password, TestToken);

        check.Account.ShouldBeNull();
    }

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

    [Fact]
    public async Task AnUnconfirmedEmailAccount_StillCostsOnePasswordVerificationFromTheConfiguredHasher()
    {
        var user = GivenAKnownAccount();
        _signInManager.CheckPasswordSignInAsync(user, _password, true).Returns(SignInResult.NotAllowed);

        var check = await CreateAccounts().VerifyCredentialAsync("known@identity.test", _password, TestToken);

        check.Status.ShouldBe(CredentialCheckStatus.EmailNotConfirmed);
        _passwordHasher.Verifications.ShouldBe(1);
    }

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

        _store.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

        return user;
    }

    private AccountsService CreateAccounts() =>
        new(
            _userManager,
            _signInManager,
            Substitute.For<IAppUserDirectory>(),
            Substitute.For<IDateTimeProvider>());
}
