using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Configuration.Options;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.TwoFactor.Adapters.Services;

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

    [Fact]
    public async Task BeginSetupAsync_WhenNoKeyExistsYet_RotatesTheSecurityStamp()
    {
        var user = GivenTheAccountExists();
        string stampBefore = user.SecurityStamp!;

        await CreateEnrollment().BeginSetupAsync(_userId, TestToken);

        user.SecurityStamp.ShouldNotBe(stampBefore);
    }

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

    private sealed class ConfigurableHasher : IPasswordHasher<AppUser>
    {
        public PasswordVerificationResult NextVerification { get; set; } = PasswordVerificationResult.Failed;

        public string HashPassword(AppUser user, string password) => $"hash-of-{password}";

        public PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword) =>
            NextVerification;
    }
}
