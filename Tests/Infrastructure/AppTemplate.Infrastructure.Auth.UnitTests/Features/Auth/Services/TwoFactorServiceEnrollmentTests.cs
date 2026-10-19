using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using AppTemplate.Infrastructure.Auth.Features.Auth.Services;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Auth.Services;

/// <summary>
/// <see cref="TwoFactorService.BeginSetupAsync"/>, <see cref="TwoFactorService.ConfirmSetupAsync"/> and
/// <see cref="TwoFactorService.DisableAsync"/> over a real <see cref="UserManager{TUser}"/>, with a real
/// <see cref="AuthenticatorTokenProvider{TUser}"/> registered under
/// <see cref="TokenOptions.DefaultAuthenticatorProvider"/> — so a right code is proven right the same
/// way a real authenticator app would be checked, not assumed.
/// </summary>
public sealed class TwoFactorServiceEnrollmentTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IUserStore<AppUser> _store = (IUserStore<AppUser>)Substitute.For(
        [
            typeof(IUserStore<AppUser>),
            typeof(IUserTwoFactorStore<AppUser>),
            typeof(IUserAuthenticatorKeyStore<AppUser>),
            typeof(IUserTwoFactorRecoveryCodeStore<AppUser>),
            typeof(IUserSecurityStampStore<AppUser>),
            typeof(IUserPasswordStore<AppUser>),
        ],
        []);

    private readonly ConfigurableHasher _passwordHasher = new();
    private readonly IAppUserDirectory _directory = Substitute.For<IAppUserDirectory>();

    private string? _authenticatorKey;
    private List<string> _recoveryCodes = [];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BeginSetupAsync_WhenNoKeyExistsYet_ProvisionsAndReturnsAFreshKey()
    {
        GivenTheAccountExists();

        var started = await CreateEnrollment().BeginSetupAsync(_userId, TestToken);

        started.Status.ShouldBe(TwoFactorSetupStatus.Started);
        started.SharedKey.ShouldNotBeNullOrEmpty();
        started.AuthenticatorUri.ShouldStartWith("otpauth://totp/");
        started.AuthenticatorUri.ShouldContain($"secret={started.SharedKey}");
    }

    /// <summary>
    /// The one side effect this use case's caller accepts rather than compensates for — see
    /// <c>SetUpTwoFactorUseCase</c>.
    /// </summary>
    [Fact]
    public async Task BeginSetupAsync_WhenNoKeyExistsYet_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        string stampBefore = user.SecurityStamp!;

        await CreateEnrollment().BeginSetupAsync(_userId, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBefore);
    }

    /// <summary>
    /// A second call before confirmation must not silently swap the secret out from under a caller
    /// who already scanned the first one into an app.
    /// </summary>
    [Fact]
    public async Task BeginSetupAsync_CalledTwiceBeforeConfirmation_ReturnsTheSamePendingKey()
    {
        GivenTheAccountExists();
        var enrollment = CreateEnrollment();

        var first = await enrollment.BeginSetupAsync(_userId, TestToken);
        var second = await enrollment.BeginSetupAsync(_userId, TestToken);

        second.SharedKey.ShouldBe(first.SharedKey);
    }

    [Fact]
    public async Task BeginSetupAsync_WhenTwoFactorIsAlreadyEnabled_ReportsAlreadyEnabledAndProvisionsNothing()
    {
        var user = GivenTheAccountExists();
        user.TwoFactorEnabled = true;

        var started = await CreateEnrollment().BeginSetupAsync(_userId, TestToken);

        started.Status.ShouldBe(TwoFactorSetupStatus.AlreadyEnabled);
        started.SharedKey.ShouldBeNull();
        _authenticatorKey.ShouldBeNull();
    }

    /// <summary>A key the store refused to keep must not be handed out to be scanned.</summary>
    [Fact]
    public async Task BeginSetupAsync_WhenTheStoreRefusesToProvisionTheKey_ReportsRejectedAndHandsBackNoKey()
    {
        GivenTheAccountExists();
        GivenTheStoreRefusesUpdates();

        var started = await CreateEnrollment().BeginSetupAsync(_userId, TestToken);

        started.Status.ShouldBe(TwoFactorSetupStatus.Rejected);
        started.SharedKey.ShouldBeNull();
        started.AuthenticatorUri.ShouldBeNull();
    }

    /// <summary>A token that outlived its account is a status, not a 500.</summary>
    [Fact]
    public async Task BeginSetupAsync_AnAccountThatNoLongerExists_ReportsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var started = await CreateEnrollment().BeginSetupAsync(_userId, TestToken);

        started.Status.ShouldBe(TwoFactorSetupStatus.NoSuchAccount);
        started.SharedKey.ShouldBeNull();
    }

    [Fact]
    public async Task ConfirmSetupAsync_AnAccountThatNoLongerExists_ReportsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(_userId, "correct horse battery", "123456", TestToken);

        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.NoSuchAccount);
        confirmation.RecoveryCodes.ShouldBeNull();
    }

    /// <summary>
    /// The code was right, so the refusal is the store's, and reporting it as a wrong code would
    /// mislead the caller. The codes already stored are not handed out: with the factor off they
    /// redeem nothing, and the next confirmation replaces them.
    /// </summary>
    [Fact]
    public async Task ConfirmSetupAsync_WhenTheStoreRefusesToArmTheFactor_ReportsRejectedAndHandsOutNoRecoveryCodes()
    {
        GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;
        GivenTheStoreRefusesUpdatesOnceTheFactorIsArmed();

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(
            _userId, "correct horse battery", AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.Rejected);
        confirmation.RecoveryCodes.ShouldBeNull();
    }

    /// <summary>
    /// Armed without recovery codes, losing the authenticator app would be losing the account, so the
    /// codes are stored first and a refusal there leaves the factor off and is reported.
    /// </summary>
    [Fact]
    public async Task ConfirmSetupAsync_WhenTheStoreRefusesTheRecoveryCodes_ArmsNothingAndReportsRejected()
    {
        var user = GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;
        GivenTheStoreRefusesTheNextUpdateAfterReplacingRecoveryCodes();

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(
            _userId, "correct horse battery", AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.Rejected);
        confirmation.RecoveryCodes.ShouldBeNull();
        user.TwoFactorEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// A second confirmation on an armed account would replace the recovery codes and then write the
    /// switch-on again; a refusal of that write would leave the account with codes nobody was shown.
    /// </summary>
    [Fact]
    public async Task ConfirmSetupAsync_WhenTwoFactorIsAlreadyEnabled_ReportsAlreadyEnabledAndKeepsTheRecoveryCodes()
    {
        var user = GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        user.TwoFactorEnabled = true;
        _recoveryCodes = ["kept-1", "kept-2"];
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;
        GivenTheStoreRefusesTheSwitchOnWriteAfterSavingRecoveryCodes();

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(
            _userId, "correct horse battery", AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        _recoveryCodes.ShouldBe(["kept-1", "kept-2"]);
        confirmation.RecoveryCodes.ShouldBeNull();
        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.AlreadyEnabled);
    }

    [Fact]
    public async Task ConfirmSetupAsync_ARightPasswordAndCode_EnablesTwoFactor()
    {
        var user = GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(
            _userId,
            "correct horse battery",
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.Confirmed);
        user.TwoFactorEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmSetupAsync_ARightPasswordAndCode_MintsTheConfiguredNumberOfRecoveryCodes()
    {
        GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        var confirmation = await CreateEnrollment(recoveryCodeCount: 6).ConfirmSetupAsync(
            _userId,
            "correct horse battery",
            AuthenticatorCodes.CurrentCodeFor(sharedKey),
            TestToken);

        confirmation.RecoveryCodes!.Count.ShouldBe(6);
        confirmation.RecoveryCodes.ShouldBeUnique();
    }

    /// <summary>
    /// The security stamp already rotated inside <c>ResetAuthenticatorKeyAsync</c> when the key was
    /// first provisioned; confirming rotates it again — see <c>ConfirmTwoFactorSetupUseCase</c> for
    /// why that second rotation is the one this feature actually relies on.
    /// </summary>
    [Fact]
    public async Task ConfirmSetupAsync_ARightPasswordAndCode_RotatesTheSecurityStampAgain()
    {
        var user = GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        string stampAfterBegin = user.SecurityStamp!;
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        await CreateEnrollment().ConfirmSetupAsync(
            _userId, "correct horse battery", AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        user.SecurityStamp.ShouldNotBe(stampAfterBegin);
    }

    [Fact]
    public async Task ConfirmSetupAsync_ARightPasswordButAWrongCode_IsRefusedAndEnablesNothing()
    {
        var user = GivenTheAccountExists();
        await GivenAPendingKeyAsync();
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(_userId, "correct horse battery", "000000", TestToken);

        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.InvalidCode);
        user.TwoFactorEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// The gap this repository was asked to close: a caller holding nothing but a stolen access token
    /// must not be able to arm two-factor sign-in — and revoke every session on the account —
    /// without proving the password, exactly as it already could not disarm one. See
    /// <see cref="DisableAsync_AWrongPassword_LeavesTwoFactorOn"/> for the mirror case.
    /// </summary>
    [Fact]
    public async Task ConfirmSetupAsync_AWrongPassword_IsRefusedAndEnablesNothingRegardlessOfTheCode()
    {
        var user = GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        _passwordHasher.NextVerification = PasswordVerificationResult.Failed;

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(
            _userId, "wrong password", AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.IncorrectPassword);
        user.TwoFactorEnabled.ShouldBeFalse();
    }

    /// <summary>A wrong password never even reaches the code check — see <c>ConfirmSetupAsync</c>.</summary>
    [Fact]
    public async Task ConfirmSetupAsync_AWrongPassword_DoesNotRotateTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        string sharedKey = await GivenAPendingKeyAsync();
        string stampAfterBegin = user.SecurityStamp!;
        _passwordHasher.NextVerification = PasswordVerificationResult.Failed;

        await CreateEnrollment().ConfirmSetupAsync(
            _userId, "wrong password", AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        user.SecurityStamp.ShouldBe(stampAfterBegin);
    }

    /// <summary>The caller already authenticated as this id — see <c>AccountsService.ChangePasswordAsync</c>.</summary>
    [Fact]
    public async Task ConfirmSetupAsync_AnAccountWithNoPasswordHash_IsReportedAsAnIncorrectPassword()
    {
        var user = GivenTheAccountExists();
        user.PasswordHash = null;
        string sharedKey = await GivenAPendingKeyAsync();

        var confirmation = await CreateEnrollment().ConfirmSetupAsync(
            _userId, "correct horse battery", AuthenticatorCodes.CurrentCodeFor(sharedKey), TestToken);

        confirmation.Status.ShouldBe(TwoFactorConfirmationStatus.IncorrectPassword);
    }

    [Fact]
    public async Task DisableAsync_ARightPassword_TurnsTwoFactorOff()
    {
        var user = GivenTheAccountExists();
        user.TwoFactorEnabled = true;
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        var disabled = await CreateEnrollment().DisableAsync(_userId, "correct horse battery", TestToken);

        disabled.ShouldBe(TwoFactorDisableStatus.Disabled);
        user.TwoFactorEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// So that a later re-enrollment starts from a fresh secret instead of the one every previous
    /// authenticator app on file for this account still knows.
    /// </summary>
    [Fact]
    public async Task DisableAsync_ARightPassword_InvalidatesThePreviousSharedKey()
    {
        var user = GivenTheAccountExists();
        string oldKey = await GivenAPendingKeyAsync();
        user.TwoFactorEnabled = true;
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;

        await CreateEnrollment().DisableAsync(_userId, "correct horse battery", TestToken);

        _authenticatorKey.ShouldNotBe(oldKey);
    }

    /// <summary>
    /// A refusal is reported, so the use case neither records the factor as off nor revokes sessions
    /// for a change that did not happen — and the secret every authenticator app knows is kept.
    /// </summary>
    [Fact]
    public async Task DisableAsync_WhenTheStoreRefuses_ReportsRejectedAndKeepsTheSharedKey()
    {
        var user = GivenTheAccountExists();
        string key = await GivenAPendingKeyAsync();
        user.TwoFactorEnabled = true;
        _passwordHasher.NextVerification = PasswordVerificationResult.Success;
        GivenTheStoreRefusesUpdates();

        var disabled = await CreateEnrollment().DisableAsync(_userId, "correct horse battery", TestToken);

        disabled.ShouldBe(TwoFactorDisableStatus.Rejected);
        _authenticatorKey.ShouldBe(key);
    }

    [Fact]
    public async Task DisableAsync_AWrongPassword_LeavesTwoFactorOn()
    {
        var user = GivenTheAccountExists();
        user.TwoFactorEnabled = true;
        _passwordHasher.NextVerification = PasswordVerificationResult.Failed;

        var disabled = await CreateEnrollment().DisableAsync(_userId, "wrong password", TestToken);

        disabled.ShouldBe(TwoFactorDisableStatus.IncorrectPassword);
        user.TwoFactorEnabled.ShouldBeTrue();
    }

    /// <summary>A token that outlived its account is not a wrong password.</summary>
    [Fact]
    public async Task DisableAsync_AnAccountThatNoLongerExists_ReportsNoSuchAccount()
    {
        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((AppUser?)null);

        var disabled = await CreateEnrollment().DisableAsync(_userId, "correct horse battery", TestToken);

        disabled.ShouldBe(TwoFactorDisableStatus.NoSuchAccount);
    }

    private void GivenTheStoreRefusesUpdates() =>
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()));

    private void GivenTheStoreRefusesUpdatesOnceTheFactorIsArmed() =>
        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ((AppUser)callInfo[0]!).TwoFactorEnabled
                ? IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure())
                : IdentityResult.Success);

    private void GivenTheStoreRefusesTheNextUpdateAfterReplacingRecoveryCodes()
    {
        bool codesJustReplaced = false;

        ((IUserTwoFactorRecoveryCodeStore<AppUser>)_store)
            .ReplaceCodesAsync(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                codesJustReplaced = true;
                return Task.CompletedTask;
            });

        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(_ => codesJustReplaced
                ? IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure())
                : IdentityResult.Success);
    }

    /// <summary>
    /// The write that saves the new recovery codes succeeds; the one after it, which switches the
    /// factor on, is refused.
    /// </summary>
    private void GivenTheStoreRefusesTheSwitchOnWriteAfterSavingRecoveryCodes()
    {
        int updatesSinceCodesReplaced = -1;

        ((IUserTwoFactorRecoveryCodeStore<AppUser>)_store)
            .ReplaceCodesAsync(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _recoveryCodes = [.. (IEnumerable<string>)callInfo[1]!];
                updatesSinceCodesReplaced = 0;
                return Task.CompletedTask;
            });

        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(_ => updatesSinceCodesReplaced < 0 || updatesSinceCodesReplaced++ == 0
                ? IdentityResult.Success
                : IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()));
    }

    private async Task<string> GivenAPendingKeyAsync()
    {
        var started = await CreateEnrollment().BeginSetupAsync(_userId, TestToken);
        return started.SharedKey!;
    }

    private AppUser GivenTheAccountExists()
    {
        var user = new AppUser
        {
            Id = _userId,
            UserName = "someone",
            Email = "someone@identity.test",
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
            PasswordHash = "stored-hash",
        };

        _directory.FindByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);

        _store.UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        var twoFactorStore = (IUserTwoFactorStore<AppUser>)_store;
        twoFactorStore.GetTwoFactorEnabledAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((AppUser)callInfo[0]!).TwoFactorEnabled));
        twoFactorStore.SetTwoFactorEnabledAsync(Arg.Any<AppUser>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                ((AppUser)callInfo[0]!).TwoFactorEnabled = (bool)callInfo[1]!;
                return Task.CompletedTask;
            });

        var authenticatorKeyStore = (IUserAuthenticatorKeyStore<AppUser>)_store;
        authenticatorKeyStore.GetAuthenticatorKeyAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_authenticatorKey));
        authenticatorKeyStore.SetAuthenticatorKeyAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _authenticatorKey = (string)callInfo[1]!;
                return Task.CompletedTask;
            });

        var recoveryCodeStore = (IUserTwoFactorRecoveryCodeStore<AppUser>)_store;
        recoveryCodeStore.ReplaceCodesAsync(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _recoveryCodes = [.. (IEnumerable<string>)callInfo[1]!];
                return Task.CompletedTask;
            });
        recoveryCodeStore.CountCodesAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_recoveryCodes.Count));

        var securityStampStore = (IUserSecurityStampStore<AppUser>)_store;
        securityStampStore.GetSecurityStampAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((AppUser)callInfo[0]!).SecurityStamp));
        securityStampStore.SetSecurityStampAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                ((AppUser)callInfo[0]!).SecurityStamp = (string)callInfo[1]!;
                return Task.CompletedTask;
            });

        var passwordStore = (IUserPasswordStore<AppUser>)_store;
        passwordStore.GetPasswordHashAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((AppUser)callInfo[0]!).PasswordHash));
        passwordStore.HasPasswordAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(true);

        return user;
    }

    private TwoFactorService CreateEnrollment(int recoveryCodeCount = 10)
    {
        var userManager = new UserManager<AppUser>(
            _store,
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            _passwordHasher,
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<AppUser>>.Instance);

        userManager.RegisterTokenProvider(TokenOptions.DefaultAuthenticatorProvider, new AuthenticatorTokenProvider<AppUser>());

        var options = new OptionsWrapper<TwoFactorOptions>(new TwoFactorOptions { RecoveryCodeCount = recoveryCodeCount });

        return new TwoFactorService(userManager, _directory, Substitute.For<IDateTimeProvider>(), options);
    }

    /// <summary>A hasher whose verification result the test controls — see <c>AccountsServiceChangePasswordTests</c>.</summary>
    private sealed class ConfigurableHasher : IPasswordHasher<AppUser>
    {
        public PasswordVerificationResult NextVerification { get; set; } = PasswordVerificationResult.Failed;

        public string HashPassword(AppUser user, string password) => $"hash-of-{password}";

        public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword) =>
            NextVerification;
    }
}
