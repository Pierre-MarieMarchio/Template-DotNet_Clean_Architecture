using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

public sealed class PurgeAbandonedRegistrationsUseCaseTests
{
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// The trap this whole class of use case exists around. The worker registers a
    /// <c>BackgroundCurrentUser</c> whose <c>UserId</c> throws rather than pretend an anonymous
    /// caller, so a maintenance pass that reached for the caller would fail on its first scheduled
    /// iteration — in production, and nowhere else, because every other host supplies one.
    /// <para>
    /// A use case has no ambient route to the caller: the only way to read one is to take it in the
    /// constructor, so the constructor is where the guarantee can be stated. Adding
    /// <see cref="ICurrentUser"/> to this use case turns this red before the worker ever runs it.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePass_NeverReadsTheCurrentUser()
    {
        var parameters = typeof(PurgeAbandonedRegistrationsUseCase)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .ToList();

        parameters.ShouldNotBeEmpty(
            "A use case with no constructor parameters would satisfy the assertion below for free.");

        parameters.ShouldNotContain(parameter => parameter.ParameterType == typeof(ICurrentUser));
    }

    [Fact]
    public async Task AnAbandonedRegistration_IsRemovedAndCommitted()
    {
        var abandoned = AbandonedFile();
        GivenTheRepositoryReturns(abandoned);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        _repository.Received(1).Remove(abandoned);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Announced so any bytes a client did deposit without ever confirming are reclaimed promptly.
    /// Nothing depends on the announcement arriving — the orphan sweep covers the same ground — but
    /// not raising it would leave every abandoned deposit to that slower path for no reason.
    /// </summary>
    [Fact]
    public async Task AnAbandonedRegistration_AnnouncesItsDeletion()
    {
        var abandoned = AbandonedFile();
        GivenTheRepositoryReturns(abandoned);

        await UseCase().ExecuteAsync(TestToken);

        abandoned.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
    }

    /// <summary>
    /// The query is the coarse filter and <c>StoredFile.IsAbandoned</c> is the decision. A row the
    /// query returned that the aggregate does not consider abandoned is left alone — which is what
    /// keeps a second host, a maintenance endpoint or a future caller reaching the same aggregate by
    /// another route from getting a different answer.
    /// </summary>
    [Fact]
    public async Task ARegistrationTheAggregateDoesNotCallAbandoned_IsLeftAlone()
    {
        var young = AStoredFile.PendingOwnedBy(Guid.CreateVersion7(), registeredAt: _now.AddMinutes(-1));
        GivenTheRepositoryReturns(young);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        _repository.DidNotReceive().Remove(Arg.Any<StoredFile>());
        young.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyBatch_CommitsNothing()
    {
        GivenTheRepositoryReturns();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABatchWithNothingActuallyAbandoned_CommitsNothing()
    {
        GivenTheRepositoryReturns(AStoredFile.PendingOwnedBy(Guid.CreateVersion7(), registeredAt: _now));

        await UseCase().ExecuteAsync(TestToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The pass belongs to no owner: it removes whatever is stale, across every account, which is
    /// exactly why there is nobody to ask about permission.
    /// </summary>
    [Fact]
    public async Task ThePass_RemovesStaleRegistrationsOfEveryOwner()
    {
        var first = AbandonedFile();
        var second = AbandonedFile();
        GivenTheRepositoryReturns(first, second);

        var result = await UseCase().ExecuteAsync(TestToken);

        first.OwnerId.ShouldNotBe(second.OwnerId);
        result.Value.ShouldBe(2);
        _repository.Received(1).Remove(first);
        _repository.Received(1).Remove(second);
    }

    [Fact]
    public async Task TheCutOff_IsTheAbandonmentWindowBeforeNow()
    {
        GivenTheRepositoryReturns();

        await UseCase().ExecuteAsync(TestToken);

        await _repository.Received(1).GetPendingRegisteredBeforeAsync(
            _now - PurgeAbandonedRegistrationsUseCase.AbandonedAfter,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>One pass pulls a bounded batch; a backlog beyond it waits for the next run.</summary>
    [Fact]
    public async Task ThePass_AsksForABoundedBatch()
    {
        GivenTheRepositoryReturns();

        await UseCase().ExecuteAsync(TestToken);

        await _repository.Received(1).GetPendingRegisteredBeforeAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Is<int>(batchSize => batchSize > 0 && batchSize <= 1_000),
            Arg.Any<CancellationToken>());
    }

    private static StoredFile AbandonedFile() =>
        AStoredFile.PendingOwnedBy(
            Guid.CreateVersion7(),
            registeredAt: _now - PurgeAbandonedRegistrationsUseCase.AbandonedAfter - TimeSpan.FromMinutes(1));

    private void GivenTheRepositoryReturns(params StoredFile[] files) =>
        _repository.GetPendingRegisteredBeforeAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(files);

    private PurgeAbandonedRegistrationsUseCase UseCase() => new(
        _repository,
        _unitOfWork,
        new StubDateTimeProvider(_now));
}
