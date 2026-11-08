using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

public sealed class PurgeExpiredIdempotencyKeysTests
{
    private readonly IIdempotencyStore _store = Substitute.For<IIdempotencyStore>();
    private static readonly DateTimeOffset _instant = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(_instant);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private PurgeExpiredIdempotencyKeysCommandHandler UseCase() => new(_store, _clock);

    [Fact]
    public async Task ExecuteAsync_PassesTheClocksInstant_ToTheStore()
    {
        await UseCase().Handle(new PurgeExpiredIdempotencyKeysCommand(), TestToken);

        await _store.Received(1).PurgeExpiredAsync(_instant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_TheStoresCount()
    {
        _store.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(7);

        var result = await UseCase().Handle(new PurgeExpiredIdempotencyKeysCommand(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(7);
    }
}
