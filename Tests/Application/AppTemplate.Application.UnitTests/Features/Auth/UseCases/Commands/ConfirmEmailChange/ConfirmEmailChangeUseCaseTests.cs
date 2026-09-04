using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.EmailChangeTokens;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IEmailChangeTokensService _emailChangeTokens = Substitute.For<IEmailChangeTokensService>();
    private readonly IRefreshTokenGrantsService _refreshTokens = Substitute.For<IRefreshTokenGrantsService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new ConfirmEmailChangeCommand("new@example.com", "a-token"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _emailChangeTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "a-token")]
    [InlineData("new@example.com", "")]
    public async Task AMalformedRequest_NeverRedeemsAnything(string newEmail, string token)
    {
        var result = await UseCase().ExecuteAsync(new ConfirmEmailChangeCommand(newEmail, token), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _emailChangeTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuccessfulChange_Succeeds()
    {
        GivenTheOutcomeIs(EmailChangeConfirmationOutcome.Changed);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Every way redeeming can fail except a rejected address, collapsed to one error.</summary>
    [Theory]
    [MemberData(nameof(GenericRefusals))]
    public async Task ABadTokenOrAMissingAccount_AnswerWithTheSameError(EmailChangeConfirmationStatus outcome)
    {
        GivenTheOutcomeIs(new EmailChangeConfirmationOutcome(outcome));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Validation(
            "auth.changeEmail.invalid",
            "The email change link is invalid or has expired."));
    }

    public static TheoryData<EmailChangeConfirmationStatus> GenericRefusals =>
        [EmailChangeConfirmationStatus.NoSuchAccount, EmailChangeConfirmationStatus.InvalidToken];

    [Fact]
    public async Task ARejectedNewAddress_ReportsTheStoresMessage()
    {
        GivenTheOutcomeIs(EmailChangeConfirmationOutcome.Rejected("That address is not allowed."));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["email"].ShouldContain("That address is not allowed.");
    }

    /// <summary>
    /// The security stamp already rotated inside ChangeEmailAsync and takes every access token with
    /// it; refresh tokens survive that rotation, so this use case has to revoke them itself.
    /// </summary>
    [Fact]
    public async Task ASuccessfulChange_RevokesEveryRefreshTokenForTheAccount()
    {
        GivenTheOutcomeIs(EmailChangeConfirmationOutcome.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulChange_RecordsTheSecurityStampRotation()
    {
        GivenTheOutcomeIs(EmailChangeConfirmationOutcome.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    [Fact]
    public async Task AFailedChange_RevokesNothing()
    {
        GivenTheOutcomeIs(EmailChangeConfirmationOutcome.InvalidToken);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private static ConfirmEmailChangeCommand ARequest() => new("new@example.com", "a-token");

    private void GivenTheOutcomeIs(EmailChangeConfirmationOutcome confirmation) =>
        _emailChangeTokens.RedeemAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(confirmation);

    private ConfirmEmailChangeUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_emailChangeTokens, _refreshTokens, _securityEventLog, currentUser, new ConfirmEmailChangeCommandValidator());

    private ConfirmEmailChangeUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
