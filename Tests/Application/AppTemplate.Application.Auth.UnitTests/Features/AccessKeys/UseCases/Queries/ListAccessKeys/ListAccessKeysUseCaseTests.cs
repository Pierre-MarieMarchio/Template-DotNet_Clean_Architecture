using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Queries.ListAccessKeys;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.AccessKeys.UseCases.Queries.ListAccessKeys;

public sealed class ListAccessKeysUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IAccessKeysService _accessKeys = Substitute.For<IAccessKeysService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await new ListAccessKeysUseCase(_accessKeys, StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCaller_GetsTheirOwnLiveKeys()
    {
        LiveAccessKey[] keys =
        [
            new(Guid.CreateVersion7(), "CI", "ak_abcdefgh", ["read"], StubDateTimeProvider.DefaultInstant, null, null),
        ];
        _accessKeys.ListLiveAsync(_callerId, Arg.Any<CancellationToken>()).Returns(keys);

        var result = await new ListAccessKeysUseCase(_accessKeys, StubCurrentUser.WithId(_callerId))
            .ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(keys);
    }
}
