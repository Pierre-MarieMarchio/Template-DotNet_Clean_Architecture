using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

namespace AppTemplate.Api.UnitTests.Features.Files.Jobs;

internal sealed class FakeAbandonedRegistrationPurge : IPurgeAbandonedRegistrationsUseCase
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeAbandonedRegistrationPurge(Exception? failure = null, int count = 3)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_failure is not null)
        {
            return Task.FromException<Result<int>>(_failure);
        }

        return Task.FromResult(Result.Success(_count));
    }
}

internal sealed class FakeOrphanedContentSweep : IReclaimOrphanedContentUseCase
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeOrphanedContentSweep(Exception? failure = null, int count = 5)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_failure is not null)
        {
            return Task.FromException<Result<int>>(_failure);
        }

        return Task.FromResult(Result.Success(_count));
    }
}

internal sealed class HangingOrphanedContentSweep : IReclaimOrphanedContentUseCase
{
    private volatile bool _hasEntered;

    public bool HasEntered => _hasEntered;

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _hasEntered = true;

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}
