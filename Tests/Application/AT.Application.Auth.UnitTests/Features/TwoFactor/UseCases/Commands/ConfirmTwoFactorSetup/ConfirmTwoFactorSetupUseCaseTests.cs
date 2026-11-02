using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;

public sealed class ConfirmTwoFactorSetupUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITwoFactorService _twoFactor = Substitute.For<ITwoFactorService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous, StubCurrentSession.WithoutSession).ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _twoFactor.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankPassword_NeverReachesTheTwoFactorPort()
    {
        var result = await UseCase().ExecuteAsync(new ConfirmTwoFactorSetupCommand("", "123456"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _twoFactor.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankCode_NeverReachesTheTwoFactorPort()
    {
        var result = await UseCase().ExecuteAsync(
            new ConfirmTwoFactorSetupCommand("correct horse battery", ""), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _twoFactor.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AWrongPassword_IsRefused()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.IncorrectPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
    }

    [Fact]
    public async Task AWrongPassword_ArmsNothingAndRevokesNoRefreshTokenAndRecordsNoEvent()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.IncorrectPassword);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARightPasswordButAWrongCode_IsRefused()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.InvalidCode);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["code"].ShouldContain("The verification code is incorrect or has expired.");
    }

    [Fact]
    public async Task ARightPasswordButAWrongCode_RevokesNoRefreshTokenAndRecordsNoEvent()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.InvalidCode);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsAnsweredAsUnauthenticatedAndRevokesNothing()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountAlreadyArmed_IsAConflictAndRevokesNothingRecordsNothing()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.AlreadyEnabled);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.alreadyEnabled");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AStoreRefusal_IsReportedAsSuchAndArmsNothingRevokesNothingRecordsNothing()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.enableRejected");
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARightPasswordAndCode_AnswersWithTheFreshRecoveryCodes()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Confirmed(["ABCDE-12345", "FGHIJ-67890"]));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RecoveryCodes.ShouldBe(["ABCDE-12345", "FGHIJ-67890"]);
    }

    [Fact]
    public async Task ARightPasswordAndCode_RecordsThatTwoFactorSignInIsNowArmed()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Confirmed(["a code"]));

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.TwoFactorEnabled(_callerId));
    }

    [Fact]
    public async Task ARightPasswordAndCode_RevokesEverySessionButTheCallersOwn()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Confirmed(["a code"]));

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            _currentSession.SessionId,
            Arg.Any<CancellationToken>());
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    [Fact]
    public async Task ARightPasswordAndCodeFromATokenWithoutASession_RevokesEverySession()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Confirmed(["a code"]));

        await UseCaseFor(StubCurrentUser.WithId(_callerId), StubCurrentSession.WithoutSession)
            .ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            null,
            Arg.Any<CancellationToken>());
    }

    private static ConfirmTwoFactorSetupCommand ARequest() => new("correct horse battery", "123456");

    private void GivenTheOutcomeIs(TwoFactorConfirmationOutcome confirmation) =>
        _twoFactor.ConfirmSetupAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(confirmation);

    private readonly StubCurrentSession _currentSession = StubCurrentSession.InSession(Guid.CreateVersion7());

    private ConfirmTwoFactorSetupUseCase UseCaseFor(ICurrentUser currentUser, StubCurrentSession currentSession) =>
        new(_twoFactor, _sessionTokens, _securityEventLog, currentUser, currentSession, new ConfirmTwoFactorSetupCommandValidator());

    private ConfirmTwoFactorSetupUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId), _currentSession);
}
