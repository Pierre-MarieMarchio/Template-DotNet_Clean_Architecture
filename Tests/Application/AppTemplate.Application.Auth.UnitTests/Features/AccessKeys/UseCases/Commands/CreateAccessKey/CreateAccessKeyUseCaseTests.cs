using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.AccessKeys.UseCases.Commands.CreateAccessKey;

public sealed class CreateAccessKeyUseCaseTests
{
    private const string _password = "the password";

    private static readonly TimeSpan _window = DeleteMyAccountUseCase.RecentAuthenticationWindow;

    private static readonly string[] _bothScopes = ["read", "write"];

    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly IAccessKeysService _accessKeys = Substitute.For<IAccessKeysService>();
    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly IReAuthenticationService _reAuthentication = Substitute.For<IReAuthenticationService>();
    private readonly ICurrentAccessToken _currentAccessToken = Substitute.For<ICurrentAccessToken>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnInvalidRequest_NeverReachesThePort()
    {
        var result = await UseCase().ExecuteAsync(new CreateAccessKeyCommand("CI", ["admin"], null, _password), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AnExpiryNotInTheFuture_IsRefusedNamingTheField(int secondsFromNow)
    {
        GivenTheProfile(hasPassword: true);
        GivenThePasswordIs(_password);

        var result = await UseCase().ExecuteAsync(
            new CreateAccessKeyCommand("CI", ["read"], _now.AddSeconds(secondsFromNow), _password), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!.Keys.ShouldBe(["expiresAt"]);
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefused()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountWithAPassword_ButNoPasswordPresented_IsRefusedNamingTheField()
    {
        GivenTheProfile(hasPassword: true);

        var result = await UseCase().ExecuteAsync(new CreateAccessKeyCommand("CI", ["read"], null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(ValidationError.Code);
        result.Error.Details!.Keys.ShouldBe(["currentPassword"]);
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountWithAPassword_AndTheWrongPassword_IsRefused_EvenWithAFreshToken()
    {
        GivenTheProfile(hasPassword: true);
        GivenThePasswordIs(_password);
        _currentAccessToken.IssuedAt.Returns(_now);

        var result = await UseCase().ExecuteAsync(new CreateAccessKeyCommand("CI", ["read"], null, "not it"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!.Keys.ShouldBe(["currentPassword"]);
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AProoflessAccount_WithAStaleAccessToken_IsAskedToSignInAgain()
    {
        GivenTheProfile(hasPassword: false);
        _currentAccessToken.IssuedAt.Returns(_now - _window - TimeSpan.FromSeconds(1));

        var result = await UseCase().ExecuteAsync(new CreateAccessKeyCommand("CI", ["read"], null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.reAuthenticationRequired");
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AProoflessAccount_WithNoIssuedAtClaim_IsAskedToSignInAgain()
    {
        GivenTheProfile(hasPassword: false);
        _currentAccessToken.IssuedAt.Returns((DateTimeOffset?)null);

        var result = await UseCase().ExecuteAsync(new CreateAccessKeyCommand("CI", ["read"], null, null), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.reAuthenticationRequired");
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AProoflessAccount_WithAFreshToken_CreatesAKeyWithoutAPassword()
    {
        GivenTheProfile(hasPassword: false);
        _currentAccessToken.IssuedAt.Returns(_now - _window + TimeSpan.FromSeconds(1));
        GivenTheAccountHolds(0);
        GivenTheKeyIsIssued();

        var result = await UseCase().ExecuteAsync(new CreateAccessKeyCommand("CI", ["read"], null, null), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _reAuthentication.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountAtTheLimit_IsRefused_AndNothingIsIssued()
    {
        GivenTheProfile(hasPassword: true);
        GivenThePasswordIs(_password);
        GivenTheAccountHolds(AccessKeyPolicy.MaxActiveKeysPerAccount);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.accessKey.limitReached");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await _accessKeys.DidNotReceiveWithAnyArgs().IssueAsync(
            default, "", [], null, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountOneBelowTheLimit_GetsItsKey()
    {
        GivenTheProfile(hasPassword: true);
        GivenThePasswordIs(_password);
        GivenTheAccountHolds(AccessKeyPolicy.MaxActiveKeysPerAccount - 1);
        GivenTheKeyIsIssued();

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AProvedCaller_GetsTheKeyOnce_WithWhatItAskedFor_AndTheFactIsRecorded()
    {
        GivenTheProfile(hasPassword: true);
        GivenThePasswordIs(_password);
        GivenTheAccountHolds(0);
        var issued = GivenTheKeyIsIssued();
        var expiresAt = _now.AddDays(30);

        var result = await UseCase().ExecuteAsync(
            new CreateAccessKeyCommand("CI", _bothScopes, expiresAt, _password), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new CreateAccessKeyOutcome(
            issued.Id, "CI", issued.Prefix, result.Value.Scopes, issued.CreatedAt, expiresAt, issued.Key));
        result.Value.Scopes.ShouldBe(_bothScopes);
        await _accessKeys.Received(1).IssueAsync(
            _callerId,
            "CI",
            Arg.Is<IReadOnlyList<string>>(scopes => scopes.SequenceEqual(_bothScopes)),
            expiresAt,
            Arg.Any<CancellationToken>());
        _securityEventLog.Received(1).Record(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.AccessKeyCreated && e.UserId == _callerId));
    }

    private static CreateAccessKeyCommand ARequest() => new("CI", ["read"], null, _password);

    private void GivenTheProfile(bool hasPassword) =>
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(
                _callerId, "a@example.org", true, [], _now, TwoFactorEnabled: false, HasPassword: hasPassword));

    private void GivenThePasswordIs(string password)
    {
        _reAuthentication.VerifyPasswordAsync(_callerId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _reAuthentication.VerifyPasswordAsync(_callerId, password, Arg.Any<CancellationToken>()).Returns(true);
    }

    private void GivenTheAccountHolds(int liveKeys) =>
        _accessKeys.CountLiveAsync(_callerId, Arg.Any<CancellationToken>()).Returns(liveKeys);

    private IssuedAccessKey GivenTheKeyIsIssued()
    {
        var issued = new IssuedAccessKey(Guid.CreateVersion7(), "ak_the-secret", "ak_the-secr", _now);

        _accessKeys.IssueAsync(
                _callerId,
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(issued);

        return issued;
    }

    private CreateAccessKeyUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private CreateAccessKeyUseCase UseCaseFor(StubCurrentUser caller) =>
        new(
            _accessKeys,
            _accounts,
            _reAuthentication,
            _currentAccessToken,
            new StubDateTimeProvider(),
            _securityEventLog,
            caller,
            new CreateAccessKeyCommandValidator());
}
