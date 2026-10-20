using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeyRegistration;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.BeginPasskeyRegistration;

public sealed class BeginPasskeyRegistrationUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly IPasskeyService _passkeys = Substitute.For<IPasskeyService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefused()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheChallengeThePortBuilds_IsHandedBackUnchanged()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(_callerId, "a@example.org", true, [], DateTimeOffset.Parse("2026-10-01T10:00:00Z"), false, true));
        var challenge = new PasskeyRegistrationChallenge("{\"rp\":{}}", "the-state");
        _passkeys.BeginRegistrationAsync(_callerId, Arg.Any<CancellationToken>()).Returns(challenge);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(challenge);
    }

    private BeginPasskeyRegistrationUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private BeginPasskeyRegistrationUseCase UseCaseFor(StubCurrentUser caller) =>
        new(_accounts, _passkeys, caller);
}
