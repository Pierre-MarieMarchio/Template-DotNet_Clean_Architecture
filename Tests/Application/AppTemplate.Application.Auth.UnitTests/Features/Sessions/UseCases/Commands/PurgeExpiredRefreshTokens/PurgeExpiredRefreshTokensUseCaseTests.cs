using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;

public sealed class PurgeExpiredRefreshTokensUseCaseTests
{
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly StubDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private PurgeExpiredRefreshTokensUseCase UseCase() => new(_sessionTokens, _clock);

    [Fact]
    public async Task ExecuteAsync_PassesTheClocksInstant_ToTheMaintenancePort()
    {
        await UseCase().ExecuteAsync(TestToken);

        await _sessionTokens.Received(1)
            .PurgeExpiredRefreshTokensAsync(StubDateTimeProvider.DefaultInstant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_ThePortsCount()
    {
        _sessionTokens.PurgeExpiredRefreshTokensAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(3);
    }
}
