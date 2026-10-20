using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Passkeys.Adapters.Services;

/// <summary>
/// <see cref="PasskeyService"/> over a real <see cref="UserManager{TUser}"/> and a substituted
/// <see cref="IPasskeyHandler{TUser}"/>: the ceremonies' arithmetic is the framework's and is not
/// retested here — what these prove is the glue the adapter owns. The account the ceremony's
/// state names is the account the credential lands on, a credential the account does not hold is
/// not removed, a vanished account is not an error where its owner is only reading, and a
/// sign-in's new signature counter is persisted before the account is handed back. And the state
/// joining a ceremony's two calls: sealed, time-limited, bound to its ceremony kind, and spent
/// before the handler ever sees it — over a real data protector and a substituted spent-ceremony
/// table, whose single-use guarantee the database makes and the integration suite asserts.
/// </summary>
public sealed class PasskeyServiceTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddYears(56);
    private static readonly TimeSpan _lifetime = new PasskeyOptions().CeremonyLifetime;

    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly ISpentPasskeyCeremonyTable _spentCeremonies = Substitute.For<ISpentPasskeyCeremonyTable>();

    // At the machine's own time, because the time-limited protector reads that clock and this one
    // must agree with it for a state to be sealed and unsealed at all.
    private readonly MovableDateTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private readonly IUserStore<AppUser> _store = (IUserStore<AppUser>)Substitute.For(
        [
            typeof(IUserStore<AppUser>),
            typeof(IUserPasskeyStore<AppUser>),
            typeof(IUserSecurityStampStore<AppUser>),
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

        // Every state is fresh unless a test says it was spent already.
        _spentCeremonies.TrySpendAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
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
        challenge.RegistrationState.ShouldNotContain("the-state");
        Unseal(RegistrationProtector(_userId), challenge.RegistrationState).ShouldBe("the-state");

        await _handler.Received(1).MakeCreationOptionsAsync(
            Arg.Is<PasskeyUserEntity>(entity =>
                entity.Id == _userId.ToString()
                && entity.Name == "a@example.org"
                && entity.DisplayName == "a@example.org"),
            Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ACeremonyThatDoesNotVerify_IsRefused()
    {
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(PasskeyAttestationResult.Fail(new PasskeyException("no")));

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", RegistrationState(), null, TestToken);

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
            _userId, "the-credential", RegistrationState(), null, TestToken);

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
            _userId, "the-credential", RegistrationState(), null, TestToken);

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
            _userId, "the-credential", RegistrationState(), "YubiKey 5", TestToken);

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
            .CompleteRegistrationAsync(_userId, "the-credential", RegistrationState(), null, TestToken);

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
        await StampStore.DidNotReceiveWithAnyArgs().SetSecurityStampAsync(default!, default!, TestToken);
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

    /// <summary>
    /// Identity's own <c>RemovePasskeyAsync</c> leaves the stamp alone, so the rotation that ends
    /// the access tokens in circulation is the adapter's to make — after the removal, never for a
    /// credential it did not remove.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ACredentialTheAccountHolds_RotatesTheSecurityStampAfterRemovingIt()
    {
        GivenTheAccountExists();
        Store.FindPasskeyAsync(Arg.Any<AppUser>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(APasskey("YubiKey 5", _now));
        Store.RemovePasskeyAsync(Arg.Any<AppUser>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await CreateService().RemoveAsync(_userId, [1, 2, 3], TestToken);

        Received.InOrder(() =>
        {
            Store.RemovePasskeyAsync(Arg.Any<AppUser>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
            StampStore.SetSecurityStampAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
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
        challenge.AuthenticationState.ShouldNotContain("the-state");
        Unseal(SignInProtector(), challenge.AuthenticationState).ShouldBe("the-state");
        // No account is named: the authenticator offers a discoverable credential.
        await _handler.Received(1).MakeRequestOptionsAsync(null, Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task CompleteSignInAsync_ACeremonyThatDoesNotVerify_IsRefused()
    {
        _handler.PerformAssertionAsync(Arg.Any<PasskeyAssertionContext>())
            .Returns(PasskeyAssertionResult.Fail<AppUser>(new PasskeyException("no")));

        var assertion = await CreateService().CompleteSignInAsync("the-credential", SignInState(), TestToken);

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

        var assertion = await CreateService().CompleteSignInAsync("the-credential", SignInState(), TestToken);

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
            .CompleteSignInAsync("the-credential", SignInState(), TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.StoreRejected);
    }

    /// <summary>
    /// What the handler receives is what it issued, so the sealing is invisible to the ceremony:
    /// the client carries the sealed form, the handler only ever reads its own.
    /// </summary>
    [Fact]
    public async Task CompleteSignInAsync_HandsTheHandlerTheStateItIssued()
    {
        GivenAVerifiedSignIn();

        await CreateService().CompleteSignInAsync("the-credential", SignInState(), TestToken);

        await _handler.Received(1).PerformAssertionAsync(
            Arg.Is<PasskeyAssertionContext>(context => context.AssertionState == "the-state"));
    }

    [Fact]
    public async Task CompleteSignInAsync_AStateThisApiNeverSealed_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();

        // The handler's own state, unsealed: what a client that forged one from the options it
        // was given would post.
        var assertion = await CreateService().CompleteSignInAsync("the-credential", "the-state", TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    [Fact]
    public async Task CompleteSignInAsync_ATamperedState_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();
        string sealedState = SignInState();

        // One character changed in the middle, where the ciphertext and its tag are.
        int middle = sealedState.Length / 2;
        char replacement = sealedState[middle] == 'A' ? 'B' : 'A';
        string tampered = string.Concat(sealedState.AsSpan(0, middle), [replacement], sealedState.AsSpan(middle + 1));

        var assertion = await CreateService().CompleteSignInAsync("the-credential", tampered, TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    /// <summary>The module's clock is the one the lifetime is read against, as for every expiry here.</summary>
    [Fact]
    public async Task CompleteSignInAsync_AStatePastItsLifetime_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();
        string sealedState = SignInState();

        _clock.Advance(_lifetime);

        var assertion = await CreateService().CompleteSignInAsync("the-credential", sealedState, TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    /// <summary>
    /// And the machine's clock too, which the protector reads on its own: a state sealed to expire
    /// before now is refused even by a module clock that has not reached its expiry.
    /// </summary>
    [Fact]
    public async Task CompleteSignInAsync_AStateTheProtectorFindsExpired_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();
        string expired = SignInProtector().Protect("the-state", DateTimeOffset.UtcNow.AddMinutes(-1));
        var behind = new MovableDateTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-10));

        var assertion = await CreateService(clock: behind).CompleteSignInAsync("the-credential", expired, TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    [Fact]
    public async Task CompleteSignInAsync_ARegistrationState_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();

        var assertion = await CreateService().CompleteSignInAsync("the-credential", RegistrationState(), TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ASignInState_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedRegistration();

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", SignInState(), null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    /// <summary>A registration state is sealed for its own account, so another's caller cannot even spend it.</summary>
    [Fact]
    public async Task CompleteRegistrationAsync_AStateSealedForAnotherAccount_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedRegistration();

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", RegistrationState(Guid.CreateVersion7()), null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    [Fact]
    public async Task CompleteRegistrationAsync_AStatePastItsLifetime_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedRegistration();
        string sealedState = RegistrationState();

        _clock.Advance(_lifetime);

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", sealedState, null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    /// <summary>
    /// The replay itself: a state the table says is spent never reaches the handler, however
    /// well the ceremony would verify — the signature counter is not what stops it.
    /// </summary>
    [Fact]
    public async Task CompleteSignInAsync_ASpentState_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();
        GivenEveryStateIsSpent();

        var assertion = await CreateService().CompleteSignInAsync("the-credential", SignInState(), TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await _handler.DidNotReceiveWithAnyArgs().PerformAssertionAsync(default!);
    }

    [Fact]
    public async Task CompleteRegistrationAsync_ASpentState_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedRegistration();
        GivenEveryStateIsSpent();

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", RegistrationState(), null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.InvalidCeremony);
        await _handler.DidNotReceiveWithAnyArgs().PerformAttestationAsync(default!);
        await Store.DidNotReceiveWithAnyArgs().AddOrUpdatePasskeyAsync(default!, default!, TestToken);
    }

    /// <summary>
    /// Spent first, verified second, so a ceremony that fails still uses its state up: a caller
    /// cannot keep one state alive by presenting credentials that do not verify.
    /// </summary>
    [Fact]
    public async Task CompleteSignInAsync_SpendsTheStateBeforeVerifying_EvenWhenTheCeremonyFails()
    {
        _handler.PerformAssertionAsync(Arg.Any<PasskeyAssertionContext>())
            .Returns(PasskeyAssertionResult.Fail<AppUser>(new PasskeyException("no")));

        await CreateService().CompleteSignInAsync("the-credential", SignInState(), TestToken);

        Received.InOrder(() =>
        {
            _spentCeremonies.TrySpendAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
            _handler.PerformAssertionAsync(Arg.Any<PasskeyAssertionContext>());
        });
    }

    [Fact]
    public async Task CompleteRegistrationAsync_SpendsTheStateBeforeVerifying_EvenWhenTheCeremonyFails()
    {
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(PasskeyAttestationResult.Fail(new PasskeyException("no")));

        await CreateService().CompleteRegistrationAsync(_userId, "the-credential", RegistrationState(), null, TestToken);

        Received.InOrder(() =>
        {
            _spentCeremonies.TrySpendAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
            _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>());
        });
    }

    /// <summary>
    /// The record is keyed by a hash of the state, never the state, and lives as long as the state
    /// does. Two states for two ceremonies never share a key.
    /// </summary>
    [Fact]
    public async Task CompleteSignInAsync_RecordsAHashOfTheStateUntilTheStateExpires()
    {
        GivenAVerifiedSignIn();
        var expiresAt = _clock.UtcNow.Add(_lifetime);

        await CreateService().CompleteSignInAsync("the-credential", SignInState(), TestToken);
        await CreateService().CompleteSignInAsync("the-credential", SignInState("another-state"), TestToken);

        var spends = _spentCeremonies.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISpentPasskeyCeremonyTable.TrySpendAsync))
            .Select(call => call.GetArguments())
            .ToList();

        spends.Count.ShouldBe(2);
        spends.ShouldAllBe(arguments => ((string)arguments[0]!).Length == 43);
        spends[0][0].ShouldNotBe(spends[1][0]);
        spends.ShouldAllBe(arguments => (DateTimeOffset)arguments[1]! == expiresAt);
    }

    /// <summary>
    /// The sweep runs where the table grows, and keeps a record a while past its state's expiry so
    /// an instance whose clock runs behind still finds it.
    /// </summary>
    [Fact]
    public async Task BeginSignInAsync_PurgesTheRecordsOfStatesLongExpired()
    {
        _handler.MakeRequestOptionsAsync(Arg.Any<AppUser?>(), Arg.Any<HttpContext>())
            .Returns(new PasskeyRequestOptionsResult { RequestOptionsJson = "{}", AssertionState = "the-state" });

        await CreateService().BeginSignInAsync(TestToken);

        await _spentCeremonies.Received(1).PurgeExpiredAsync(
            Arg.Is<DateTimeOffset>(cutoff => cutoff < _clock.UtcNow),
            Arg.Any<CancellationToken>());
    }

    private IUserPasskeyStore<AppUser> Store => (IUserPasskeyStore<AppUser>)_store;

    private IUserSecurityStampStore<AppUser> StampStore => (IUserSecurityStampStore<AppUser>)_store;

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

    private void GivenAVerifiedSignIn()
    {
        Store.AddOrUpdatePasskeyAsync(Arg.Any<AppUser>(), Arg.Any<UserPasskeyInfo>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _handler.PerformAssertionAsync(Arg.Any<PasskeyAssertionContext>())
            .Returns(PasskeyAssertionResult.Success(APasskey("YubiKey 5", _now), TheUser()));
    }

    private void GivenAVerifiedRegistration()
    {
        GivenTheAccountExists();
        Store.AddOrUpdatePasskeyAsync(Arg.Any<AppUser>(), Arg.Any<UserPasskeyInfo>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _handler.PerformAttestationAsync(Arg.Any<PasskeyAttestationContext>())
            .Returns(Succeeded(_userId.ToString()));
    }

    private void GivenEveryStateIsSpent() =>
        _spentCeremonies.TrySpendAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

    private async Task ShouldNeitherSpendNorVerifyAsync()
    {
        await _spentCeremonies.DidNotReceiveWithAnyArgs().TrySpendAsync(default!, default, TestToken);
        await _handler.DidNotReceiveWithAnyArgs().PerformAssertionAsync(default!);
        await _handler.DidNotReceiveWithAnyArgs().PerformAttestationAsync(default!);
    }

    private ITimeLimitedDataProtector RegistrationProtector(Guid userId) =>
        _dataProtection
            .CreateProtector(PasskeyService.RegistrationStatePurpose, userId.ToString("N"))
            .ToTimeLimitedDataProtector();

    private ITimeLimitedDataProtector SignInProtector() =>
        _dataProtection.CreateProtector(PasskeyService.SignInStatePurpose).ToTimeLimitedDataProtector();

    /// <summary>A registration state as this API seals it, for <see cref="_userId"/> unless told otherwise.</summary>
    private string RegistrationState(Guid? userId = null) =>
        RegistrationProtector(userId ?? _userId).Protect("the-state", _clock.UtcNow.Add(_lifetime));

    private string SignInState(string state = "the-state") =>
        SignInProtector().Protect(state, _clock.UtcNow.Add(_lifetime));

    private static string Unseal(ITimeLimitedDataProtector protector, string sealedState) =>
        protector.Unprotect(sealedState, out _);

    private PasskeyService CreateService(
        IUserValidator<AppUser>[]? validators = null,
        MovableDateTimeProvider? clock = null)
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

        return new PasskeyService(
            userManager,
            _handler,
            _httpContextAccessor,
            _directory,
            _dataProtection,
            _spentCeremonies,
            clock ?? _clock,
            new OptionsWrapper<PasskeyOptions>(new PasskeyOptions()));
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
