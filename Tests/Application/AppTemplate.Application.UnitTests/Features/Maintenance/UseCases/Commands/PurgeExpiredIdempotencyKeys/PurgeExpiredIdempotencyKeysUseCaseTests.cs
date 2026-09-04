using AppTemplate.Application.Common.Idempotency;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public sealed class PurgeExpiredIdempotencyKeysUseCaseTests
{
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private readonly FixedDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private PurgeExpiredIdempotencyKeysUseCase UseCase() => new(_store, _clock);

    [Fact]
    public async Task ExecuteAsync_PassesTheClocksInstant_ToTheStore()
    {
        await UseCase().ExecuteAsync(TestToken);

        await _store.Received(1).PurgeExpiredAsync(FixedDateTimeProvider.DefaultInstant, Arg.Any<CancellationToken>());
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
