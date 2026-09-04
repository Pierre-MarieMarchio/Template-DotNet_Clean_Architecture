using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands;

public sealed class ResetPasswordUseCaseTests
{
    private readonly IPasswordResetTokens _resetTokens = Substitute.For<IPasswordResetTokens>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly ResetPasswordUseCase _useCase;

    public ResetPasswordUseCaseTests() =>
        _useCase = new ResetPasswordUseCase(
            _resetTokens,
            _refreshTokens,
            _securityEventLog,
            new ResetPasswordCommandValidator());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", "a-token", "correct horse battery")]
    [InlineData("someone@example.com", "", "correct horse battery")]
    [InlineData("someone@example.com", "a-token", "")]
    public async Task AMalformedRequest_NeverRedeemsAnything(string email, string token, string newPassword)
    {
        var result = await _useCase.ExecuteAsync(new ResetPasswordCommand(email, token, newPassword), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _resetTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuccessfulReset_Succeeds()
    {
        var userId = Guid.CreateVersion7();
        GivenTheOutcomeIs(PasswordReset.Succeeded(userId));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>An unknown address and an invalid or expired token must not be distinguishable.</summary>
    [Theory]
    [MemberData(nameof(EnumerationRefusals))]
    public async Task AnUnknownAddressOrABadToken_AnswerWithTheSameError(PasswordResetOutcome outcome)
    {
        GivenTheOutcomeIs(new PasswordReset(outcome, null, null));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Validation(
            "auth.resetPassword.invalid",
            "The password reset link is invalid or has expired."));
    }

    public static TheoryData<PasswordResetOutcome> EnumerationRefusals =>
        [PasswordResetOutcome.NoSuchAccount, PasswordResetOutcome.InvalidToken];

    [Fact]
    public async Task ARejectedNewPassword_ReportsTheStoresMessage()
    {
        GivenTheOutcomeIs(PasswordReset.Rejected("Passwords must have at least one digit."));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["password"].ShouldContain("Passwords must have at least one digit.");
    }

    /// <summary>
    /// The store already rotated the security stamp as part of redeeming the token, which fails
    /// every access token; refresh tokens survive that rotation and are revoked here.
    /// </summary>
    [Fact]
    public async Task ASuccessfulReset_RevokesEveryRefreshTokenForTheAccount()
    {
        var userId = Guid.CreateVersion7();
        GivenTheOutcomeIs(PasswordReset.Succeeded(userId));

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulReset_RecordsTheSecurityStampRotation()
    {
        var userId = Guid.CreateVersion7();
        GivenTheOutcomeIs(PasswordReset.Succeeded(userId));

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(userId));
    }

    [Fact]
    public async Task AFailedReset_RevokesNothing()
    {
        GivenTheOutcomeIs(PasswordReset.InvalidToken);

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private static ResetPasswordCommand ARequest() => new("someone@example.com", "a-token", "correct horse battery");

    private void GivenTheOutcomeIs(PasswordReset reset) =>
        _resetTokens.RedeemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(reset);
}
