using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetMySecurityEvents;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.SecurityEvents.UseCases.Queries.GetMySecurityEvents;

public sealed class GetMySecurityEventsUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ISecurityEventQueries _queries = Substitute.For<ISecurityEventQueries>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public GetMySecurityEventsUseCaseTests() =>
        _queries.GetForUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<PageRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<SecurityEventEntry>([], 1, 20, 0));

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetMySecurityEventsQuery(1, 20), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(new GetMySecurityEventsQuery(1, 20), TestToken);

        await _queries.DidNotReceive().GetForUserAsync(
            Arg.Any<Guid>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePage_IsAlwaysScopedToTheCaller()
    {
        await UseCase().ExecuteAsync(new GetMySecurityEventsQuery(1, 20), TestToken);

        await _queries.Received(1).GetForUserAsync(
            _callerId, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APageSizeAboveTheCeiling_IsRefused()
    {
        var result = await UseCase().ExecuteAsync(
            new GetMySecurityEventsQuery(1, SecurityEventCollectionPolicy.Instance.MaxPageSize + 1),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
        await _queries.DidNotReceive().GetForUserAsync(
            Arg.Any<Guid>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    private GetMySecurityEventsUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_queries, currentUser);

    private GetMySecurityEventsUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
