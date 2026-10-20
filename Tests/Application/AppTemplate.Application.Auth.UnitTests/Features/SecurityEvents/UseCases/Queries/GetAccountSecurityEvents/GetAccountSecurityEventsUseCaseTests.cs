using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using FluentValidation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;

public sealed class GetAccountSecurityEventsUseCaseTests
{
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly ISecurityEventQueries _queries = Substitute.For<ISecurityEventQueries>();
    private readonly IValidator<GetAccountSecurityEventsQuery> _validator = new GetAccountSecurityEventsQueryValidator();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public GetAccountSecurityEventsUseCaseTests() =>
        _queries.GetForUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<PageRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<SecurityEventEntry>([], 1, 20, 0));

    [Fact]
    public async Task AnEmptyAccountId_IsRefusedBeforeAnythingIsQueried()
    {
        var result = await UseCase().ExecuteAsync(new GetAccountSecurityEventsQuery(Guid.Empty, 1, 20), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("request.validationFailed");
        await _queries.DidNotReceive().GetForUserAsync(
            Arg.Any<Guid>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePage_IsScopedToTheNamedAccount()
    {
        await UseCase().ExecuteAsync(new GetAccountSecurityEventsQuery(_targetId, 1, 20), TestToken);

        await _queries.Received(1).GetForUserAsync(
            _targetId, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APageSizeAboveTheCeiling_IsRefused()
    {
        var result = await UseCase().ExecuteAsync(
            new GetAccountSecurityEventsQuery(_targetId, 1, SecurityEventCollectionPolicy.Instance.MaxPageSize + 1),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
        await _queries.DidNotReceive().GetForUserAsync(
            Arg.Any<Guid>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwarded()
    {
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new GetAccountSecurityEventsQuery(_targetId, 1, 20), cancellation.Token);

        await _queries.Received(1).GetForUserAsync(
            _targetId, Arg.Any<PageRequest>(), cancellation.Token);
    }

    private GetAccountSecurityEventsUseCase UseCase() => new(_queries, _validator);
}
