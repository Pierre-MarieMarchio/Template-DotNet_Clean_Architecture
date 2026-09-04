using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.UserProfiles;
using AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Queries.GetCurrentUser;

public sealed class GetCurrentUserUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IUserProfilesService _profiles = Substitute.For<IUserProfilesService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
        _profiles.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheProfile_IsReadForTheCallersOwnId()
    {
        _profiles.FindByIdAsync(_callerId, Arg.Any<CancellationToken>()).Returns(AProfile());

        await UseCase().ExecuteAsync(TestToken);

        await _profiles.Received(1).FindByIdAsync(_callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExistingProfile_IsReturnedInFull()
    {
        var createdAt = DateTimeOffset.UtcNow;
        _profiles.FindByIdAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(new UserProfile(_callerId, "someone", "someone@example.com", true, ["Administrator"], createdAt, true));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(_callerId);
        result.Value.UserName.ShouldBe("someone");
        result.Value.Email.ShouldBe("someone@example.com");
        result.Value.EmailConfirmed.ShouldBeTrue();
        result.Value.Roles.ShouldBe(["Administrator"]);
        result.Value.CreatedAt.ShouldBe(createdAt);
        result.Value.TwoFactorEnabled.ShouldBeTrue();
    }

    /// <summary>
    /// The account named by a valid token can still be gone by the time this runs — deleted after
    /// the token was issued. Nothing about the caller's own principal is trusted for the answer.
    /// </summary>
    [Fact]
    public async Task AnAccountThatNoLongerExists_IsRefused()
    {
        _profiles.FindByIdAsync(_callerId, Arg.Any<CancellationToken>()).Returns((UserProfile?)null);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task TheCancellationToken_IsForwarded()
    {
        _profiles.FindByIdAsync(_callerId, Arg.Any<CancellationToken>()).Returns(AProfile());
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(cancellation.Token);

        await _profiles.Received(1).FindByIdAsync(_callerId, cancellation.Token);
    }

    private static UserProfile AProfile() =>
        new(_callerId, "someone", "someone@example.com", true, [], DateTimeOffset.UtcNow, false);

    private GetCurrentUserUseCase UseCaseFor(ICurrentUser currentUser) => new(_profiles, currentUser);

    private GetCurrentUserUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
