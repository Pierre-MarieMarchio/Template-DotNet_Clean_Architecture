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

public sealed class PasskeyServiceTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddYears(56);
    private static readonly TimeSpan _lifetime = new PasskeyOptions().CeremonyLifetime;

    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly ISpentPasskeyCeremonyTable _spentCeremonies = Substitute.For<ISpentPasskeyCeremonyTable>();

    private readonly MovableDateTimeProvider _clockAtTheMachinesTime = new(DateTimeOffset.UtcNow);

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

        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(IdentityResult.Success));

        GivenEveryStateIsFreshUnlessATestSpendsIt();
    }

    private void GivenEveryStateIsFreshUnlessATestSpendsIt() =>
        _spentCeremonies.TrySpendAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

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

        var assertion = await CreateService().CompleteSignInAsync("the-credential", "the-state", TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    [Fact]
    public async Task CompleteSignInAsync_ATamperedState_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();
        string sealedState = SignInState();

        int middle = sealedState.Length / 2;
        char replacement = sealedState[middle] == 'A' ? 'B' : 'A';
        string tampered = string.Concat(sealedState.AsSpan(0, middle), [replacement], sealedState.AsSpan(middle + 1));

        var assertion = await CreateService().CompleteSignInAsync("the-credential", tampered, TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

    [Fact]
    public async Task CompleteSignInAsync_AStatePastItsLifetime_IsRefusedBeforeTheHandler()
    {
        GivenAVerifiedSignIn();
        string sealedState = SignInState();

        _clockAtTheMachinesTime.Advance(_lifetime);

        var assertion = await CreateService().CompleteSignInAsync("the-credential", sealedState, TestToken);

        assertion.Status.ShouldBe(PasskeySignInStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

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

        _clockAtTheMachinesTime.Advance(_lifetime);

        var registration = await CreateService().CompleteRegistrationAsync(
            _userId, "the-credential", sealedState, null, TestToken);

        registration.Status.ShouldBe(PasskeyRegistrationStatus.InvalidCeremony);
        await ShouldNeitherSpendNorVerifyAsync();
    }

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

    [Fact]
    public async Task CompleteSignInAsync_RecordsAHashOfTheStateUntilTheStateExpires()
    {
        GivenAVerifiedSignIn();
        var expiresAt = _clockAtTheMachinesTime.UtcNow.Add(_lifetime);

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

    [Fact]
    public async Task BeginSignInAsync_PurgesTheRecordsOfStatesLongExpired()
    {
        _handler.MakeRequestOptionsAsync(Arg.Any<AppUser?>(), Arg.Any<HttpContext>())
            .Returns(new PasskeyRequestOptionsResult { RequestOptionsJson = "{}", AssertionState = "the-state" });

        await CreateService().BeginSignInAsync(TestToken);

        await _spentCeremonies.Received(1).PurgeExpiredAsync(
            Arg.Is<DateTimeOffset>(cutoff => cutoff < _clockAtTheMachinesTime.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginSignInAsync_KeepsASpentRecordFiveMinutesPastItsStatesExpiry()
    {
        var ledger = new SpentCeremonyLedger();
        GivenAVerifiedSignIn();
        _handler.MakeRequestOptionsAsync(Arg.Any<AppUser?>(), Arg.Any<HttpContext>())
            .Returns(new PasskeyRequestOptionsResult { RequestOptionsJson = "{}", AssertionState = "another-state" });
        var clock = new MovableDateTimeProvider(_clockAtTheMachinesTime.UtcNow);

        await CreateService(clock: clock, spentCeremonies: ledger)
            .CompleteSignInAsync("the-credential", SignInState(), TestToken);
        ledger.Count.ShouldBe(1);

        clock.Advance(_lifetime + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        await CreateService(clock: clock, spentCeremonies: ledger).BeginSignInAsync(TestToken);

        ledger.Count.ShouldBe(
            1,
            "a second short of five minutes past the state's expiry, another instance whose clock runs " +
            "behind may still accept the state, so its spent record has to survive the purge.");

        clock.Advance(TimeSpan.FromSeconds(1));
        await CreateService(clock: clock, spentCeremonies: ledger).BeginSignInAsync(TestToken);

        ledger.Count.ShouldBe(0, "five minutes past the state's expiry, the grace is over and the record goes.");
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
            .CreateProtector(PasskeyCeremonyStateIssuer.RegistrationStatePurpose, userId.ToString("N"))
            .ToTimeLimitedDataProtector();

    private ITimeLimitedDataProtector SignInProtector() =>
        _dataProtection.CreateProtector(PasskeyCeremonyStateIssuer.SignInStatePurpose).ToTimeLimitedDataProtector();

    private string RegistrationState(Guid? userId = null) =>
        RegistrationProtector(userId ?? _userId).Protect("the-state", _clockAtTheMachinesTime.UtcNow.Add(_lifetime));

    private string SignInState(string state = "the-state") =>
        SignInProtector().Protect(state, _clockAtTheMachinesTime.UtcNow.Add(_lifetime));

    private static string Unseal(ITimeLimitedDataProtector protector, string sealedState) =>
        protector.Unprotect(sealedState, out _);

    private PasskeyService CreateService(
        IUserValidator<AppUser>[]? validators = null,
        MovableDateTimeProvider? clock = null,
        ISpentPasskeyCeremonyTable? spentCeremonies = null)
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
            new PasskeyCeremonyStateIssuer(
                _dataProtection,
                spentCeremonies ?? _spentCeremonies,
                clock ?? _clockAtTheMachinesTime,
                new OptionsWrapper<PasskeyOptions>(new PasskeyOptions())));
    }
}

file sealed class SpentCeremonyLedger : ISpentPasskeyCeremonyTable
{
    private readonly Dictionary<string, DateTimeOffset> _spent = new(StringComparer.Ordinal);

    public int Count => _spent.Count;

    public Task<bool> TrySpendAsync(
        string stateHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_spent.TryAdd(stateHash, expiresAt));

    public Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var expired = _spent.Where(record => record.Value <= cutoff).Select(record => record.Key).ToList();

        expired.ForEach(stateHash => _spent.Remove(stateHash));

        return Task.FromResult(expired.Count);
    }
}

file sealed class RefusingValidator : IUserValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "refused", Description = "refused" }));
}
