using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.ChangePassword;

public sealed class ChangePasswordUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous, StubCurrentSession.WithoutSession)
            .ExecuteAsync(new ChangePasswordCommand("old", "correct horse battery"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "correct horse battery")]
    [InlineData("old password", "")]
    public async Task AMalformedRequest_NeverReachesTheStore(string currentPassword, string newPassword)
    {
        var result = await UseCase().ExecuteAsync(new ChangePasswordCommand(currentPassword, newPassword), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AMatchingCurrentPassword_Succeeds()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.Changed);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsAnsweredAsUnauthenticatedAndRevokesNothing()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AWrongCurrentPassword_IsRefused()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.IncorrectCurrentPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("request.validationFailed");
        result.Error.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
    }

    [Fact]
    public async Task ARejectedNewPassword_ReportsTheStoresMessageOnTheNewPasswordField()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.Rejected("Passwords must have at least one digit."));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["newPassword"].ShouldContain("Passwords must have at least one digit.");
        result.Error.Details.ContainsKey("password").ShouldBeFalse();
    }

    [Fact]
    public async Task ASuccessfulChange_RevokesEverySessionButTheCallersOwn()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            _currentSession.SessionId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulChangeFromATokenWithoutASession_RevokesEverySession()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.Changed);

        await UseCaseFor(StubCurrentUser.WithId(_callerId), StubCurrentSession.WithoutSession)
            .ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulChange_MakesOneRevocationCall()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _sessionTokens.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .ShouldBe([nameof(ISessionTokensService.RevokeAllRefreshTokensAsync)]);
    }

    [Fact]
    public async Task ASuccessfulChange_RecordsTheSecurityStampRotation()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    [Fact]
    public async Task AWrongCurrentPassword_RevokesNothing()
    {
        GivenTheOutcomeIs(PasswordChangeOutcome.IncorrectCurrentPassword);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private static ChangePasswordCommand ARequest() => new("old password", "correct horse battery");

    private void GivenTheOutcomeIs(PasswordChangeOutcome change) =>
        _accounts.ChangePasswordAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(change);

    private readonly StubCurrentSession _currentSession = StubCurrentSession.InSession(Guid.CreateVersion7());

    private ChangePasswordUseCase UseCaseFor(ICurrentUser currentUser, StubCurrentSession currentSession) =>
        new(_accounts, _sessionTokens, _securityEventLog, currentUser, currentSession, new ChangePasswordCommandValidator());

    private ChangePasswordUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId), _currentSession);
}
