using AppTemplate.Application.Features.Auth.Ports.RefreshTokenMaintenance;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;

public sealed class PurgeExpiredRefreshTokensUseCaseTests
{
    private readonly IRefreshTokenMaintenanceService _maintenance = Substitute.For<IRefreshTokenMaintenanceService>();
    private readonly StubDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private PurgeExpiredRefreshTokensUseCase UseCase() => new(_maintenance, _clock);

    [Fact]
    public async Task ExecuteAsync_PassesTheClocksInstant_ToTheMaintenancePort()
    {
        await UseCase().ExecuteAsync(TestToken);

        await _maintenance.Received(1)
            .PurgeExpiredAsync(StubDateTimeProvider.DefaultInstant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_ThePortsCount()
    {
        _maintenance.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(3);
    }

    [Fact]
    public async Task ExecuteAsync_Forwards_TheCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(cancellation.Token);

        await _maintenance.Received(1).PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), cancellation.Token);
    }
}
