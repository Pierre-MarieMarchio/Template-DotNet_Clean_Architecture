using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;

public sealed class PurgeExpiredSecurityEventsTests
{
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly StubDateTimeProvider _clock = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private PurgeExpiredSecurityEventsCommandHandler UseCase() => new(_securityEventLog, _clock);

    [Fact]
    public async Task ExecuteAsync_PassesTheClocksInstant_ToTheTrailPort()
    {
        await UseCase().Handle(new PurgeExpiredSecurityEventsCommand(), TestToken);

        await _securityEventLog.Received(1)
            .PurgeExpiredAsync(StubDateTimeProvider.DefaultInstant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_ThePortsCount()
    {
        _securityEventLog.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(4);

        var result = await UseCase().Handle(new PurgeExpiredSecurityEventsCommand(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(4);
    }
}
