using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;

public sealed class CompletePasskeyRegistrationUseCaseTests
{
    private static readonly TimeSpan _window = DeleteMyAccountUseCase.RecentAuthenticationWindow;

    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly IPasskeyService _passkeys = Substitute.For<IPasskeyService>();
    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly IReAuthenticationService _reAuthentication = Substitute.For<IReAuthenticationService>();
    private readonly ICurrentAccessToken _currentAccessToken = Substitute.For<ICurrentAccessToken>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly IDateTimeProvider _dateTimeProvider = new StubDateTimeProvider();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankCredential_NeverReachesThePasskeyPort()
    {
        var result = await UseCase().ExecuteAsync(
            new CompletePasskeyRegistrationCommand("", "the-state", null, "the password"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefused()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountWithAPassword_ButNoPasswordPresented_IsRefusedNamingTheField()
    {
        GivenTheProfile(hasPassword: true);

        var result = await UseCase().ExecuteAsync(
            new CompletePasskeyRegistrationCommand("the-credential", "the-state", null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(ValidationError.Code);
        result.Error.Details!.Keys.ShouldBe(["currentPassword"]);
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountWithAPassword_AndTheWrongPassword_IsRefused()
    {
        GivenTheProfile(hasPassword: true);
        _reAuthentication.VerifyPasswordAsync(_callerId, "not it", Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(
            new CompletePasskeyRegistrationCommand("the-credential", "the-state", null, "not it"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!.Keys.ShouldBe(["currentPassword"]);
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AProoflessAccount_WithAStaleAccessToken_IsAskedToSignInAgain()
    {
        GivenTheProfile(hasPassword: false);
        _currentAccessToken.IssuedAt.Returns(_now - _window - TimeSpan.FromSeconds(1));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.reAuthenticationRequired");
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AProoflessAccount_WithNoIssuedAtClaim_IsAskedToSignInAgain()
    {
        GivenTheProfile(hasPassword: false);
        _currentAccessToken.IssuedAt.Returns((DateTimeOffset?)null);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.reAuthenticationRequired");
    }

    [Fact]
    public async Task ARefusedProof_NeverReachesThePasskeyPort()
    {
        GivenTheProfile(hasPassword: true);
        _reAuthentication.VerifyPasswordAsync(_callerId, "not it", Arg.Any<CancellationToken>()).Returns(false);

        await UseCase().ExecuteAsync(
            new CompletePasskeyRegistrationCommand("the-credential", "the-state", null, "not it"), TestToken);

        await _passkeys.DidNotReceiveWithAnyArgs().CompleteRegistrationAsync(
            default, "", "", null, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACeremonyThatDoesNotVerify_IsRefused_AndRecordsNothing()
    {
        GivenTheProfile(hasPassword: true);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _passkeys.CompleteRegistrationAsync(_callerId, "the-credential", "the-state", null, Arg.Any<CancellationToken>())
            .Returns(PasskeyRegistrationOutcome.InvalidCeremony());

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!.Keys.ShouldBe(["credential"]);
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARegistrationThatFindsNoAccount_IsRefusedAsUnauthenticated()
    {
        GivenTheProfile(hasPassword: true);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _passkeys.CompleteRegistrationAsync(_callerId, "the-credential", "the-state", null, Arg.Any<CancellationToken>())
            .Returns(PasskeyRegistrationOutcome.NoSuchAccount());

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AStoreRefusal_IsReported_AndRecordsNothing()
    {
        GivenTheProfile(hasPassword: true);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _passkeys.CompleteRegistrationAsync(_callerId, "the-credential", "the-state", null, Arg.Any<CancellationToken>())
            .Returns(PasskeyRegistrationOutcome.StoreRejected());

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.passkey.registrationRejected");
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AVerifiedCeremonyOnAPasswordAccount_ReturnsTheCredentialAndRecordsTheFact()
    {
        GivenTheProfile(hasPassword: true);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        var stored = new PasskeyCredential([1, 2, 3], "YubiKey 5", _now);
        _passkeys.CompleteRegistrationAsync(_callerId, "the-credential", "the-state", null, Arg.Any<CancellationToken>())
            .Returns(PasskeyRegistrationOutcome.Registered(stored));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(stored);
        _securityEventLog.Received(1).Record(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.PasskeyRegistered && e.UserId == _callerId));
    }

    [Fact]
    public async Task AProoflessAccountWithAFreshToken_CanRegisterWithoutAPassword()
    {
        GivenTheProfile(hasPassword: false);
        _currentAccessToken.IssuedAt.Returns(_now - _window + TimeSpan.FromSeconds(1));
        var stored = new PasskeyCredential([1, 2, 3], null, _now);
        _passkeys.CompleteRegistrationAsync(_callerId, "the-credential", "the-state", null, Arg.Any<CancellationToken>())
            .Returns(PasskeyRegistrationOutcome.Registered(stored));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _reAuthentication.ReceivedCalls().ShouldBeEmpty();
    }

    private static CompletePasskeyRegistrationCommand ARequest() =>
        new("the-credential", "the-state", null, "the password");

    private void GivenTheProfile(bool hasPassword) =>
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(
                _callerId, "a@example.org", true, [], _now, TwoFactorEnabled: false, HasPassword: hasPassword));

    private CompletePasskeyRegistrationUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private CompletePasskeyRegistrationUseCase UseCaseFor(StubCurrentUser caller) =>
        new(
            _passkeys,
            _accounts,
            _reAuthentication,
            _currentAccessToken,
            _dateTimeProvider,
            _securityEventLog,
            caller,
            new CompletePasskeyRegistrationCommandValidator());
}
