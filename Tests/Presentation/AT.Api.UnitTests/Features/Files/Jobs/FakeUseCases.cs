using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using Mediator;

namespace AppTemplate.Api.UnitTests.Features.Files.Jobs;

internal sealed class FakeAbandonedRegistrationPurge : ICommandHandler<PurgeAbandonedRegistrationsCommand, Result<int>>
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeAbandonedRegistrationPurge(Exception? failure = null, int count = 3)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public ValueTask<Result<int>> Handle(PurgeAbandonedRegistrationsCommand command, CancellationToken cancellationToken)
    {
        CallCount++;

        if (_failure is not null)
        {
            return ValueTask.FromException<Result<int>>(_failure);
        }

        return ValueTask.FromResult(Result.Success(_count));
    }
}

internal sealed class FakeOrphanedContentSweep : ICommandHandler<ReclaimOrphanedContentCommand, Result<int>>
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeOrphanedContentSweep(Exception? failure = null, int count = 5)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public ValueTask<Result<int>> Handle(ReclaimOrphanedContentCommand command, CancellationToken cancellationToken)
    {
        CallCount++;

        if (_failure is not null)
        {
            return ValueTask.FromException<Result<int>>(_failure);
        }

        return ValueTask.FromResult(Result.Success(_count));
    }
}

internal sealed class HangingOrphanedContentSweep : ICommandHandler<ReclaimOrphanedContentCommand, Result<int>>
{
    private volatile bool _hasEntered;

    public bool HasEntered => _hasEntered;

    public async ValueTask<Result<int>> Handle(ReclaimOrphanedContentCommand command, CancellationToken cancellationToken)
    {
        _hasEntered = true;

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}
