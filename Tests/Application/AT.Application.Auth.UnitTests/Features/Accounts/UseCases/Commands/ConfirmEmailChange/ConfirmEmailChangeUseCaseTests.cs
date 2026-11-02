using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IEmailTokensService _emailTokens = Substitute.For<IEmailTokensService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new ConfirmEmailChangeCommand("new@example.com", "a-token"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "a-token")]
    [InlineData("new@example.com", "")]
    public async Task AMalformedRequest_NeverRedeemsAnything(string newEmail, string token)
    {
        var result = await UseCase().ExecuteAsync(new ConfirmEmailChangeCommand(newEmail, token), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _emailTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuccessfulChange_Succeeds()
    {
        GivenTheOutcomeIs(EmailChangeOutcome.Changed);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ABadToken_AnswersWithTheInvalidLinkError()
    {
        GivenTheOutcomeIs(EmailChangeOutcome.InvalidToken);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Validation(
            "auth.changeEmail.invalid",
            "The email change link is invalid or has expired."));
    }

    [Fact]
    public async Task ARejectedNewAddress_ReportsTheStoresMessage()
    {
        GivenTheOutcomeIs(EmailChangeOutcome.Rejected("That address is not allowed."));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["email"].ShouldContain("That address is not allowed.");
    }

    [Fact]
    public async Task ASuccessfulChange_RevokesEveryRefreshTokenForTheAccount()
    {
        GivenTheOutcomeIs(EmailChangeOutcome.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(_callerId, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulChange_RecordsTheSecurityStampRotation()
    {
        GivenTheOutcomeIs(EmailChangeOutcome.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    [Fact]
    public async Task AFailedChange_RevokesNothing()
    {
        GivenTheOutcomeIs(EmailChangeOutcome.InvalidToken);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private static ConfirmEmailChangeCommand ARequest() => new("new@example.com", "a-token");

    private void GivenTheOutcomeIs(EmailChangeOutcome confirmation) =>
        _emailTokens.ChangeEmailAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(confirmation);

    private ConfirmEmailChangeUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_emailTokens, _sessionTokens, _securityEventLog, currentUser, new ConfirmEmailChangeCommandValidator());

    private ConfirmEmailChangeUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
