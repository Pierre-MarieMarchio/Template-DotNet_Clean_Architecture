using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactor;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

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
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ARequest(), TestToken);

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

    /// <summary>
    /// The gap this whole feature closes: a wrong password refuses arming the second factor exactly
    /// as a wrong password already refused disarming it — see <c>DisableTwoFactorUseCaseTests</c>.
    /// </summary>
    [Fact]
    public async Task AWrongPassword_IsRefused()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.IncorrectPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
    }

    /// <summary>
    /// The proof this repository asked for before shipping the fix: without it, a caller holding
    /// nothing but a stolen access token could arm two-factor sign-in — and revoke every session on
    /// the account — on a password it never had to prove.
    /// </summary>
    [Fact]
    public async Task AWrongPassword_ArmsNothingAndRevokesNoRefreshTokenAndRecordsNoEvent()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.IncorrectPassword);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, Arg.Any<CancellationToken>());
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

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A token that outlived its account — see <c>GetCurrentUserUseCase</c>.</summary>
    [Fact]
    public async Task AnAccountThatNoLongerExists_IsAnsweredAsUnauthenticatedAndRevokesNothing()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// A second confirmation leaves the armed factor and its recovery codes alone, and so has no
    /// posture change to record or revoke for.
    /// </summary>
    [Fact]
    public async Task AnAccountAlreadyArmed_IsAConflictAndRevokesNothingRecordsNothing()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.AlreadyEnabled);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.alreadyEnabled");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// The store refusing to arm the factor is not the caller's wrong code: the code was right and
    /// retrying it may well succeed.
    /// </summary>
    [Fact]
    public async Task AStoreRefusal_IsReportedAsSuchAndArmsNothingRevokesNothingRecordsNothing()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.enableRejected");
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Non-regression: the right password still arms the second factor.</summary>
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

    /// <summary>
    /// The security stamp already rotated inside <c>ConfirmSetupAsync</c> — see
    /// <c>ITwoFactorService.ConfirmSetupAsync</c> — and takes every access token with it; refresh tokens
    /// survive that rotation, so this use case has to revoke them itself.
    /// </summary>
    [Fact]
    public async Task ARightPasswordAndCode_RevokesEveryRefreshTokenForTheAccount()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Confirmed(["a code"]));

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(_callerId, Arg.Any<CancellationToken>());
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    /// <summary>
    /// Account-wide, the caller's own session included: nothing in the request names the caller's
    /// refresh-token family, so the use case has no grant it could spare.
    /// </summary>
    [Fact]
    public async Task ARightPasswordAndCode_RevokesTheCallersOwnSessionToo()
    {
        GivenTheOutcomeIs(TwoFactorConfirmationOutcome.Confirmed(["a code"]));

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _sessionTokens.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .ShouldBe([nameof(ISessionTokensService.RevokeAllRefreshTokensAsync)]);
    }

    private static ConfirmTwoFactorSetupCommand ARequest() => new("correct horse battery", "123456");

    private void GivenTheOutcomeIs(TwoFactorConfirmationOutcome confirmation) =>
        _twoFactor.ConfirmSetupAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(confirmation);

    private ConfirmTwoFactorSetupUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_twoFactor, _sessionTokens, _securityEventLog, currentUser, new ConfirmTwoFactorSetupCommandValidator());

    private ConfirmTwoFactorSetupUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
