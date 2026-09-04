using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ChangePassword;

public sealed class ChangePasswordUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IUserAccounts _accounts = Substitute.For<IUserAccounts>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
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
        GivenTheOutcomeIs(PasswordChange.Changed);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AWrongCurrentPassword_IsRefused()
    {
        GivenTheOutcomeIs(PasswordChange.IncorrectCurrentPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("request.validationFailed");
        result.Error.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
    }

    [Fact]
    public async Task ARejectedNewPassword_ReportsTheStoresMessage()
    {
        GivenTheOutcomeIs(PasswordChange.Rejected("Passwords must have at least one digit."));

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["password"].ShouldContain("Passwords must have at least one digit.");
    }

    /// <summary>
    /// The security stamp already rotated inside <c>ChangePasswordAsync</c> and takes every access
    /// token with it; refresh tokens survive that rotation, so this use case has to revoke them
    /// itself.
    /// </summary>
    [Fact]
    public async Task ASuccessfulChange_RevokesEveryRefreshTokenForTheAccount()
    {
        GivenTheOutcomeIs(PasswordChange.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulChange_RecordsTheSecurityStampRotation()
    {
        GivenTheOutcomeIs(PasswordChange.Changed);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    [Fact]
    public async Task AWrongCurrentPassword_RevokesNothing()
    {
        GivenTheOutcomeIs(PasswordChange.IncorrectCurrentPassword);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private static ChangePasswordCommand ARequest() => new("old password", "correct horse battery");

    private void GivenTheOutcomeIs(PasswordChange change) =>
        _accounts.ChangePasswordAsync(_callerId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(change);

    private ChangePasswordUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_accounts, _refreshTokens, _securityEventLog, currentUser, new ChangePasswordCommandValidator());

    private ChangePasswordUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
