using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

public sealed class PurgeAbandonedRegistrationsUseCaseTests
{
    private static readonly DateTimeOffset _now = StubDateTimeProvider.DefaultInstant;

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private StubLeaderLease _lease = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

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

    [Fact]
    public async Task AnAbandonedRegistration_AnnouncesItsDeletion()
    {
        var abandoned = AbandonedFile();
        GivenTheRepositoryReturns(abandoned);

        await UseCase().ExecuteAsync(TestToken);

        abandoned.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ARegistrationTheAggregateDoesNotCallAbandoned_IsLeftAlone()
    {
        var young = AStoredFile.PendingOwnedBy(UserId.Create(Guid.CreateVersion7()), registeredAt: _now.AddMinutes(-1));
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
        GivenTheRepositoryReturns(AStoredFile.PendingOwnedBy(UserId.Create(Guid.CreateVersion7()), registeredAt: _now));

        await UseCase().ExecuteAsync(TestToken);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

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

    [Fact]
    public async Task TheLeaseHeldByAnotherHost_LeavesThePassUndone()
    {
        GivenTheRepositoryReturns(AbandonedFile());
        _lease = StubLeaderLease.HeldElsewhere();

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(0);
        _repository.DidNotReceive().Remove(Arg.Any<StoredFile>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLeaseName_IsPinnedAsALiteral()
    {
        GivenTheRepositoryReturns();

        await UseCase().ExecuteAsync(TestToken);

        _lease.RequestedNames.ShouldBe(["apptemplate.files.purge-abandoned"]);
    }

    [Fact]
    public async Task AFailedCommit_LeavesTheRowsForTheNextPass()
    {
        GivenTheRepositoryReturns(AbandonedFile());
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(
                _ => throw new ConcurrencyConflictException("changed meanwhile"),
                _ => Task.FromResult(1));

        await Should.ThrowAsync<ConcurrencyConflictException>(() => UseCase().ExecuteAsync(TestToken));

        var retried = AbandonedFile();
        GivenTheRepositoryReturns(retried);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.Value.ShouldBe(1);
        _repository.Received(1).Remove(retried);
    }

    private static StoredFile AbandonedFile() =>
        AStoredFile.PendingOwnedBy(
            UserId.Create(Guid.CreateVersion7()),
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
        new StubDateTimeProvider(_now),
        _lease,
        NullLogger<PurgeAbandonedRegistrationsUseCase>.Instance);
}
