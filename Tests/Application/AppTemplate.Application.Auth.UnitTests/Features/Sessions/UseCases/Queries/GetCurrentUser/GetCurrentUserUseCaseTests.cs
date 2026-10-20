using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Queries.GetCurrentUser;

public sealed class GetCurrentUserUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheProfile_IsReadForTheCallersOwnId()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>()).Returns(AProfile());

        await UseCase().ExecuteAsync(TestToken);

        await _accounts.Received(1).FindProfileAsync(_callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExistingProfile_IsReturnedInFull()
    {
        var createdAt = DateTimeOffset.UtcNow;
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(_callerId, "someone@example.com", true, ["Administrator"], createdAt, true, HasPassword: true));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(_callerId);
        result.Value.Email.ShouldBe("someone@example.com");
        result.Value.EmailConfirmed.ShouldBeTrue();
        result.Value.Roles.ShouldBe(["Administrator"]);
        result.Value.CreatedAt.ShouldBe(createdAt);
        result.Value.TwoFactorEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefused()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task TheCancellationToken_IsForwarded()
    {
        _accounts.FindProfileAsync(_callerId, Arg.Any<CancellationToken>()).Returns(AProfile());
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(cancellation.Token);

        await _accounts.Received(1).FindProfileAsync(_callerId, cancellation.Token);
    }

    private static UserProfile AProfile() =>
        new(_callerId, "someone@example.com", true, [], DateTimeOffset.UtcNow, false, HasPassword: true);

    private GetCurrentUserUseCase UseCaseFor(ICurrentUser currentUser) => new(_accounts, currentUser);

    private GetCurrentUserUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
