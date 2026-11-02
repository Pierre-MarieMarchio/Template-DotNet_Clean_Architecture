using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Queries.ListPasskeys;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Queries.ListPasskeys;

public sealed class ListPasskeysUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

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
    public async Task TheCredentialsThePortLists_AreListedAsThePortReturnsThem()
    {
        var listed = new[]
        {
            new PasskeyCredential([1, 2, 3], "YubiKey 5", DateTimeOffset.Parse("2026-10-01T10:00:00Z")),
            new PasskeyCredential([4, 5], null, DateTimeOffset.Parse("2026-10-02T10:00:00Z")),
        };
        _passkeys.ListAsync(_callerId, Arg.Any<CancellationToken>()).Returns(listed);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldBe(listed);
    }

    private ListPasskeysUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private ListPasskeysUseCase UseCaseFor(StubCurrentUser caller) => new(_passkeys, caller);
}
