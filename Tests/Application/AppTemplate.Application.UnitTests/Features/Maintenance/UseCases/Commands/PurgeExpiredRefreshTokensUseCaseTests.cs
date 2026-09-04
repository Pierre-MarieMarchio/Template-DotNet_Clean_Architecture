using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Maintenance.UseCases.Commands;

public sealed class PurgeExpiredRefreshTokensUseCaseTests
{
    private readonly IRefreshTokenMaintenance _maintenance = Substitute.For<IRefreshTokenMaintenance>();
    private readonly FixedDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private PurgeExpiredRefreshTokensUseCase UseCase() => new(_maintenance, _clock);

    [Fact]
    public async Task ExecuteAsync_PassesTheClocksInstant_ToTheMaintenancePort()
    {
        await UseCase().ExecuteAsync(TestToken);

        await _maintenance.Received(1)
            .PurgeExpiredAsync(FixedDateTimeProvider.DefaultInstant, Arg.Any<CancellationToken>());
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
