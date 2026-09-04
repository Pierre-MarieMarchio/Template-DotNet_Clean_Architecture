using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

public sealed class ConfirmTwoFactorSetupUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITwoFactorEnrollment _enrollment = Substitute.For<ITwoFactorEnrollment>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _enrollment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankPassword_NeverReachesTheEnrollmentPort()
    {
        var result = await UseCase().ExecuteAsync(new ConfirmTwoFactorSetupCommand("", "123456"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _enrollment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankCode_NeverReachesTheEnrollmentPort()
    {
        var result = await UseCase().ExecuteAsync(
            new ConfirmTwoFactorSetupCommand("correct horse battery", ""), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _enrollment.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// The gap this whole feature closes: a wrong password refuses arming the second factor exactly
    /// as a wrong password already refused disarming it — see <c>DisableTwoFactorUseCaseTests</c>.
    /// </summary>
    [Fact]
    public async Task AWrongPassword_IsRefused()
    {
        GivenTheOutcomeIs(TwoFactorConfirmation.IncorrectPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
    }

    /// <summary>
    /// The proof this repository asked for before shipping the fix: without it, a caller holding
    /// nothing but a stolen access token could arm two-factor sign-in — and revoke every other
    /// session on the account — on a password it never had to prove.
    /// </summary>
    [Fact]
    public async Task AWrongPassword_ArmsNothingAndRevokesNoRefreshTokenAndRecordsNoEvent()
    {
        GivenTheOutcomeIs(TwoFactorConfirmation.IncorrectPassword);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARightPasswordButAWrongCode_IsRefused()
    {
        GivenTheOutcomeIs(TwoFactorConfirmation.InvalidCode);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["code"].ShouldContain("The verification code is incorrect or has expired.");
    }

    [Fact]
    public async Task ARightPasswordButAWrongCode_RevokesNoRefreshTokenAndRecordsNoEvent()
    {
        GivenTheOutcomeIs(TwoFactorConfirmation.InvalidCode);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Non-regression: the right password still arms the second factor.</summary>
    [Fact]
    public async Task ARightPasswordAndCode_AnswersWithTheFreshRecoveryCodes()
    {
        GivenTheOutcomeIs(TwoFactorConfirmation.Confirmed(["ABCDE-12345", "FGHIJ-67890"]));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RecoveryCodes.ShouldBe(["ABCDE-12345", "FGHIJ-67890"]);
    }

    [Fact]
    public async Task ARightPasswordAndCode_RecordsThatTwoFactorSignInIsNowArmed()
    {
        GivenTheOutcomeIs(TwoFactorConfirmation.Confirmed(["a code"]));

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.TwoFactorEnabled(_callerId));
    }

    /// <summary>
    /// The security stamp already rotated inside <c>ConfirmAsync</c> — see
    /// <c>TwoFactorEnrollment.ConfirmAsync</c> — and takes every access token with it; refresh tokens
    /// survive that rotation, so this use case has to revoke them itself.
    /// </summary>
    [Fact]
    public async Task ARightPasswordAndCode_RevokesEveryRefreshTokenForTheAccount()
    {
        GivenTheOutcomeIs(TwoFactorConfirmation.Confirmed(["a code"]));

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_callerId, Arg.Any<CancellationToken>());
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    private static ConfirmTwoFactorSetupCommand ARequest() => new("correct horse battery", "123456");

    private void GivenTheOutcomeIs(TwoFactorConfirmation confirmation) =>
        _enrollment.ConfirmAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(confirmation);

    private ConfirmTwoFactorSetupUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_enrollment, _refreshTokens, _securityEventLog, currentUser, new ConfirmTwoFactorSetupCommandValidator());

    private ConfirmTwoFactorSetupUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
