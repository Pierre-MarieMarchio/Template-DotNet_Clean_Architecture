using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Validation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.UseCases.Commands.DeleteMyAccount;

public sealed class DeleteMyAccountUseCaseTests
{
    /// <summary>The window a proofless account's access token must have been issued within.</summary>
    private static readonly TimeSpan _window = DeleteMyAccountUseCase.RecentAuthenticationWindow;

    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly IReAuthenticationService _reAuthentication = Substitute.For<IReAuthenticationService>();
    private readonly ICurrentAccessToken _currentAccessToken = Substitute.For<ICurrentAccessToken>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly IDomainEventPublisher _domainEvents = Substitute.For<IDomainEventPublisher>();
    private readonly IDateTimeProvider _dateTimeProvider = new StubDateTimeProvider();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new DeleteMyAccountCommand(null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A valid token can outlive its account: deleted after the token was issued.</summary>
    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefused()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand(null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _reAuthentication.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountWithAPassword_ButNoPasswordPresented_IsRefusedNamingTheField()
    {
        GivenTheProfile(hasPassword: true, twoFactorEnabled: false);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand(null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(ValidationError.Code);
        result.Error.Details!.Keys.ShouldBe(["currentPassword"]);
        _reAuthentication.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountWithAPassword_AndTheWrongPassword_IsRefused()
    {
        GivenTheProfile(hasPassword: true, twoFactorEnabled: false);
        _reAuthentication.VerifyPasswordAsync(_callerId, "not it", Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand("not it", null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!.Keys.ShouldBe(["currentPassword"]);
        await _accounts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAccountWithTwoFactor_ButNoCodePresented_IsRefusedNamingTheField()
    {
        GivenTheProfile(hasPassword: false, twoFactorEnabled: true);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand(null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!.Keys.ShouldBe(["twoFactorCode"]);
    }

    [Fact]
    public async Task AnAccountWithTwoFactor_AndAnInvalidCode_IsRefused()
    {
        GivenTheProfile(hasPassword: false, twoFactorEnabled: true);
        _reAuthentication.VerifyTwoFactorCodeAsync(_callerId, "000000", Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand(null, "000000"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!.Keys.ShouldBe(["twoFactorCode"]);
        await _accounts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The account that has neither a password nor an armed factor — a provider-linked one — falls
    /// back to the token itself, and only while it is fresh.
    /// </summary>
    [Fact]
    public async Task AProoflessAccount_WithAStaleAccessToken_IsAskedToSignInAgain()
    {
        GivenTheProfile(hasPassword: false, twoFactorEnabled: false);
        _currentAccessToken.IssuedAt.Returns(_now - _window - TimeSpan.FromSeconds(1));

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand(null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.reAuthenticationRequired");
        await _accounts.DidNotReceive().DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AProoflessAccount_WithNoIssuedAtClaim_IsAskedToSignInAgain()
    {
        GivenTheProfile(hasPassword: false, twoFactorEnabled: false);
        _currentAccessToken.IssuedAt.Returns((DateTimeOffset?)null);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand(null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.reAuthenticationRequired");
    }

    [Fact]
    public async Task AProoflessAccount_WithARecentlyIssuedAccessToken_Succeeds()
    {
        GivenTheProfile(hasPassword: false, twoFactorEnabled: false);
        _currentAccessToken.IssuedAt.Returns(_now - _window + TimeSpan.FromSeconds(1));
        _accounts.DeleteAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(AccountDeletionOutcome.Deleted);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand(null, null), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnArmedAccount_WithPasswordAndCodeVerified_Succeeds()
    {
        GivenTheProfile(hasPassword: true, twoFactorEnabled: true);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _reAuthentication.VerifyTwoFactorCodeAsync(_callerId, "123456", Arg.Any<CancellationToken>()).Returns(true);
        _accounts.DeleteAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(AccountDeletionOutcome.Deleted);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand("the password", "123456"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _currentAccessToken.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>The account left between the profile read and the delete: the same answer as the profile read.</summary>
    [Fact]
    public async Task ADeletionThatFindsNoAccount_IsRefusedAsUnauthenticated()
    {
        GivenTheProfile(hasPassword: true, twoFactorEnabled: false);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _accounts.DeleteAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(AccountDeletionOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand("the password", null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AStoreRefusal_IsReportedAndPublishesNothing()
    {
        GivenTheProfile(hasPassword: true, twoFactorEnabled: false);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _accounts.DeleteAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(AccountDeletionOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(new DeleteMyAccountCommand("the password", null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.deletionRejected");
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
        _domainEvents.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASuccessfulDeletion_RecordsTheFactAndPublishesIt()
    {
        GivenTheProfile(hasPassword: true, twoFactorEnabled: false);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _accounts.DeleteAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(AccountDeletionOutcome.Deleted);

        await UseCase().ExecuteAsync(new DeleteMyAccountCommand("the password", null), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.AccountSelfDeleted(_callerId));
        await _domainEvents.Received(1).PublishAsync(
            Arg.Is<UserAccountDeleted>(fact =>
                fact.UserId.Value == _callerId && fact.OccurredAt == _now),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The fact names what is already true: a deletion the store refused must never reach a
    /// consumer, because the rows the features still hold would then belong to an account that
    /// still exists.
    /// </summary>
    [Fact]
    public async Task TheFactIsPublished_OnlyAfterTheDeletionCommitted()
    {
        GivenTheProfile(hasPassword: true, twoFactorEnabled: false);
        _reAuthentication.VerifyPasswordAsync(_callerId, "the password", Arg.Any<CancellationToken>()).Returns(true);
        _accounts.DeleteAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(AccountDeletionOutcome.Deleted);

        await UseCase().ExecuteAsync(new DeleteMyAccountCommand("the password", null), TestToken);

        Received.InOrder(async () =>
        {
            await _accounts.DeleteAsync(_callerId, Arg.Any<CancellationToken>());
            await _domainEvents.PublishAsync(Arg.Any<UserAccountDeleted>(), Arg.Any<CancellationToken>());
        });
    }

    private void GivenTheProfile(bool hasPassword, bool twoFactorEnabled) =>
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(
                _callerId,
                "someone",
                "someone@example.com",
                EmailConfirmed: true,
                [],
                DateTimeOffset.UtcNow,
                twoFactorEnabled,
                hasPassword));

    private DeleteMyAccountUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(
            _accounts,
            _reAuthentication,
            _currentAccessToken,
            _securityEventLog,
            _domainEvents,
            currentUser,
            _dateTimeProvider,
            new DeleteMyAccountCommandValidator());

    private DeleteMyAccountUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
