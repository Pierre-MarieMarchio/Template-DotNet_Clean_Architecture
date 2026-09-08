using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public sealed class PurgeExpiredIdempotencyKeysUseCaseTests
{
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private readonly StubDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private sealed class StubDateTimeProvider : IDateTimeProvider
    {
        public static readonly DateTimeOffset DefaultInstant = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => DefaultInstant;
    }

    private PurgeExpiredIdempotencyKeysUseCase UseCase() => new(_store, _clock);

    [Fact]
    public async Task ExecuteAsync_PassesTheClocksInstant_ToTheStore()
    {
        await UseCase().ExecuteAsync(TestToken);

        await _store.Received(1).PurgeExpiredAsync(StubDateTimeProvider.DefaultInstant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_TheStoresCount()
    {
        _store.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(7);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(7);
    }

    [Fact]
    public async Task ExecuteAsync_Forwards_TheCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(cancellation.Token);

        await _store.Received(1).PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), cancellation.Token);
    }
}
