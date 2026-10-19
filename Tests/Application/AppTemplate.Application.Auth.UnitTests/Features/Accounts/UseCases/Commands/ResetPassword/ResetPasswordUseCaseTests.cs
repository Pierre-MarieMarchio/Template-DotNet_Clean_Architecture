using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResetPassword;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.ResetPassword;

public sealed class ResetPasswordUseCaseTests
{
    private readonly IEmailTokensService _emailTokens = Substitute.For<IEmailTokensService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly ResetPasswordUseCase _useCase;

    public ResetPasswordUseCaseTests() =>
        _useCase = new ResetPasswordUseCase(
            _emailTokens,
            _sessionTokens,
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
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuccessfulReset_Succeeds()
    {
        var userId = Guid.CreateVersion7();
        GivenTheOutcomeIs(PasswordResetOutcome.Succeeded(userId));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>An unknown address and an invalid or expired token are one refusal, so they cannot be told apart.</summary>
    [Fact]
    public async Task AnUnknownAddressOrABadToken_AnswerWithTheSameError()
    {
        GivenTheOutcomeIs(PasswordResetOutcome.InvalidToken);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Validation(
            "auth.resetPassword.invalid",
            "The password reset link is invalid or has expired."));
    }

    /// <summary>On "newPassword", the request field the value came from.</summary>
    [Fact]
    public async Task ARejectedNewPassword_ReportsTheStoresMessageOnTheNewPasswordField()
    {
        GivenTheOutcomeIs(PasswordResetOutcome.Rejected("Passwords must have at least one digit."));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["newPassword"].ShouldContain("Passwords must have at least one digit.");
        result.Error.Details.ContainsKey("password").ShouldBeFalse();
    }

    /// <summary>
    /// The store already rotated the security stamp as part of redeeming the token, which fails
    /// every access token; refresh tokens survive that rotation and are revoked here.
    /// </summary>
    [Fact]
    public async Task ASuccessfulReset_RevokesEveryRefreshTokenForTheAccount()
    {
        var userId = Guid.CreateVersion7();
        GivenTheOutcomeIs(PasswordResetOutcome.Succeeded(userId));

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(userId, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulReset_RecordsTheSecurityStampRotation()
    {
        var userId = Guid.CreateVersion7();
        GivenTheOutcomeIs(PasswordResetOutcome.Succeeded(userId));

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(userId));
    }

    [Fact]
    public async Task AFailedReset_RevokesNothing()
    {
        GivenTheOutcomeIs(PasswordResetOutcome.InvalidToken);

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private static ResetPasswordCommand ARequest() => new("someone@example.com", "a-token", "correct horse battery");

    private void GivenTheOutcomeIs(PasswordResetOutcome reset) =>
        _emailTokens.ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(reset);
}
