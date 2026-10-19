using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using AppTemplate.Infrastructure.Auth.Features.Auth.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Auth.Services;

/// <summary>
/// <see cref="PasskeyService"/> over a real <see cref="UserManager{TUser}"/> and a substituted
/// <see cref="IPasskeyHandler{TUser}"/>: the ceremonies' arithmetic is the framework's and is not
/// retested here — what these prove is the glue the adapter owns. The account the ceremony's
/// state names is the account the credential lands on, a credential the account does not hold is
/// not removed, a vanished account is not an error where its owner is only reading, and a
/// sign-in's new signature counter is persisted before the account is handed back.
/// </summary>
public sealed class PasskeyServiceTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddYears(56);

    private readonly IUserStore<AppUser> _store = (IUserStore<AppUser>)Substitute.For(
        [
            typeof(IUserStore<AppUser>),
            typeof(IUserPasskeyStore<AppUser>),
        ],
        []);

    private readonly IPasskeyHandler<AppUser> _handler = Substitute.For<IPasskeyHandler<AppUser>>();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public PasskeyServiceTests()
    {
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        // The store's update is the UserManager's own step around every passkey write; a
        // substituted IUserStore returns a null result for it, which would fault inside the
        // manager rather than say anything about this adapter.
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(IdentityResult.Success));
    }

    [Fact]
    public async Task BeginRegistrationAsync_WithoutAnAccount_IsRefused()
    {
        GivenTheAccountExists(exists: false);

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateService().BeginRegistrationAsync(_userId, TestToken));
    }

    [Fact]
    public async Task BeginRegistrationAsync_NamesTheAccountInTheUserEntityTheAuthenticatorStores()
    {
        GivenTheAccountExists();

        _handler.MakeCreationOptionsAsync(Arg.Any<PasskeyUserEntity>(), Arg.Any<HttpContext>())
            .Returns(new PasskeyCreationOptionsResult
            {
                CreationOptionsJson = "the-creation-options",
                AttestationState = "the-state",
            });

        var challenge = await CreateService().BeginRegistrationAsync(_userId, TestToken);

        challenge.CreationOptions.ShouldBe("the-creation-options");
        challenge.RegistrationState.ShouldBe("the-state");

        await _handler.Received(1).MakeCreationOptionsAsync(
            Arg.Is<PasskeyUserEntity>(entity =>
                entity.Id == _userId.ToString()
                && entity.Name == "a@example.org"
                && entity.DisplayName == "a-user"),
            Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ACeremonyThatDoesNotVerify_IsRefused()
    {
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(PasskeyAttestationResult.Fail(new PasskeyException("no")));

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", "the-state", null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.InvalidCeremony);
    }

    /// <summary>
    /// The ceremony state was issued naming this account's handle; one naming another's was not
    /// issued to this caller, and completing it would store a credential whose user handle answers
    /// a different account.
    /// </summary>
    [Fact]
    public async Task CompleteRegistrationAsync_AStateNamingAnotherAccount_IsRefused()
    {
        GivenTheAccountExists();
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(Succeeded("another-user-id"));

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", "the-state", null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.InvalidCeremony);
        await Store.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!, TestToken);
    }

    /// <summary>The account left between the re-authentication the use case demanded and the store.</summary>
    [Fact]
    public async Task CompleteRegistrationAsync_AnAccountThatNoLongerExists_IsRefused()
    {
        GivenTheAccountExists(exists: false);
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(Succeeded(_userId.ToString()));

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", "the-state", null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.NoSuchAccount);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_AVerifiedCeremony_StoresTheCredentialWithItsName()
    {
        GivenTheAccountExists();
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(Succeeded(_userId.ToString()));
        Store.AddOrUpdatePasskeyAsync(Arg.Any<AppUser>(), Arg.Any<UserPasskeyInfo>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", "the-state", "YubiKey 5", TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.Registered);
        registration.Credential!.Name.ShouldBe("YubiKey 5");
        await Store.Received(1).AddOrUpdatePasskeyAsync(
            Arg.Any<AppUser>(),
            Arg.Is<UserPasskeyInfo>(passkey => passkey.Name == "YubiKey 5"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The store write itself is void-shaped; what can refuse is the <c>UserManager</c> update
    /// that surrounds it, so the refusal is produced by a validator that rejects the update.
    /// </summary>
    [Fact]
    public async Task CompleteRegistrationAsync_AStoreRefusal_IsRefused()
    {
        GivenTheAccountExists();
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(Succeeded(_userId.ToString()));

        var registration = await CreateService(validators: [new RefusingValidator()])
            .CompleteRegistrationAsync(_userId, "the-credential", "the-state", null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.StoreRejected);
    }

    [Fact]
    public async Task ListAsync_ForAnAccountThatNoLongerExists_IsAnEmptyList()
    {
        GivenTheAccountExists(exists: false);

        var listed = await CreateService().ListAsync(_userId, TestToken);

        listed.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_ListsTheCredentialsNewestFirst()
    {
        GivenTheAccountExists();
        Store.GetPasskeysAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                APasskey("older", _now.AddDays(-1)),
                APasskey("newer", _now),
            ]);

        var listed = await CreateService().ListAsync(_userId, TestToken);

        listed.Count.ShouldBe(2);
        listed[0].Name.ShouldBe("newer");
        listed[1].Name.ShouldBe("older");
    }

    [Fact]
    public async Task RemoveAsync_ForAnAccountThatNoLongerExists_AnswersNotFound()
    {
        GivenTheAccountExists(exists: false);

        (await CreateService().RemoveAsync(_userId, [1, 2, 3], TestToken)).ShouldBeFalse();
    }

    /// <summary>
    /// The read before the remove is what makes an id the account never held and one belonging to
    /// somebody else the same answer: <see cref="IUserPasskeyStore{TUser}.FindPasskeyAsync"/> only
    /// looks among the account's own.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ACredentialTheAccountDoesNotHold_AnswersNotFoundAndRemovesNothing()
    {
        GivenTheAccountExists();
        Store.FindPasskeyAsync(Arg.Any<AppUser>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns((UserPasskeyInfo?)null);

        (await CreateService().RemoveAsync(_userId, [1, 2, 3], TestToken)).ShouldBeFalse();
        await Store.DidNotReceiveWithAnyArgs().RemovePasskeyAsync(default!, default!, TestToken);
    }

    [Fact]
    public async Task RemoveAsync_ACredentialTheAccountHolds_IsRemoved()
    {
        GivenTheAccountExists();
        Store.FindPasskeyAsync(Arg.Any<AppUser>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(APasskey("YubiKey 5", _now));
        Store.RemovePasskeyAsync(Arg.Any<AppUser>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        (await CreateService().RemoveAsync(_userId, [1, 2, 3], TestToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task BeginSignInAsync_HandsBackWhatTheHandlerBuilt()
    {
        _handler.MakeRequestOptionsAsync(Arg.Any<AppUser?>(), Arg.Any<HttpContext>())
            .Returns(new PasskeyRequestOptionsResult
            {
                RequestOptionsJson = "the-request-options",
                AssertionState = "the-state",
            });

        var challenge = await CreateService().BeginSignInAsync(TestToken);

        challenge.SignInOptions.ShouldBe("the-request-options");
        challenge.AuthenticationState.ShouldBe("the-state");
        // No account is named: the authenticator offers a discoverable credential.
        await _handler.Received(1).MakeRequestOptionsAsync(null, Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task CompleteSignInAsync_ACeremonyThatDoesNotVerify_IsRefused()
    {
        _handler.PerformAssertionAsync(Arg.Any<PasskeyAssertionContext>())
            .Returns(PasskeyAssertionResult.Fail<AppUser>(new PasskeyException("no")));

        var assertion = await CreateService().CompleteSignInAsync("the-credential", "the-state", TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
    }

    /// <summary>
    /// The counter before the account facts: a caller that mints tokens next must be reading a
    /// credential whose replay window has already closed.
    /// </summary>
    [Fact]
    public async Task CompleteSignInAsync_PersistsTheNewSignatureCounterBeforeAnswering()
    {
        var user = TheUser();
        var passkey = APasskey("YubiKey 5", _now);
        Store.AddOrUpdatePasskeyAsync(Arg.Any<AppUser>(), Arg.Any<UserPasskeyInfo>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _handler.PerformAssertionAsync(Arg.Any<PasskeyAssertionContext>())
            .Returns(PasskeyAssertionResult.Success(passkey, user));

        var assertion = await CreateService().CompleteSignInAsync("the-credential", "the-state", TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.Authenticated);
        assertion.Account!.UserId.ShouldBe(_userId);
        await Store.Received(1).AddOrUpdatePasskeyAsync(user, passkey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteSignInAsync_ACounterTheStoreRefuses_StopsTheSignIn()
    {
        var user = TheUser();
        _handler.PerformAssertionAsync(Arg.Any<PasskeyAssertionContext>())
            .Returns(PasskeyAssertionResult.Success(APasskey("YubiKey 5", _now), user));

        var assertion = await CreateService(validators: [new RefusingValidator()])
            .CompleteSignInAsync("the-credential", "the-state", TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.StoreRejected);
    }

    private IUserPasskeyStore<AppUser> Store => (IUserPasskeyStore<AppUser>)_store;

    private static AppUser TheUser() => new()
    {
        Id = _userId,
        UserName = "a-user",
        Email = "a@example.org",
    };

    private void GivenTheAccountExists(bool exists = true)
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(exists ? TheUser() : (AppUser?)null);
    }

    private static UserPasskeyInfo APasskey(string name, DateTimeOffset createdAt) => new(
        [1, 2, 3],
        publicKey: [4, 5, 6],
        createdAt: createdAt,
        signCount: 1,
        transports: [],
        isUserVerified: true,
        isBackupEligible: false,
        isBackedUp: false,
        attestationObject: [],
        clientDataJson: [])
    {
        Name = name,
    };

    private static PasskeyAttestationResult Succeeded(string userEntityId) =>
        PasskeyAttestationResult.Success(
            APasskey("a-credential", _now),
            new PasskeyUserEntity { Id = userEntityId, Name = "a@example.org", DisplayName = "a-user" });

    private PasskeyService CreateService(IUserValidator<AppUser>[]? validators = null)
    {
        var userManager = new UserManager<AppUser>(
            _store,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            Substitute.For<IPasswordHasher<AppUser>>(),
            validators ?? [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        return new PasskeyService(userManager, _handler, _httpContextAccessor, _directory);
    }
}

/// <summary>
/// Rejects every update, so the <c>UserManager</c> call that surrounds a passkey store write
/// fails — the one refusal a void-shaped store write can produce.
/// </summary>
file sealed class RefusingValidator : IUserValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "refused", Description = "refused" }));
}
