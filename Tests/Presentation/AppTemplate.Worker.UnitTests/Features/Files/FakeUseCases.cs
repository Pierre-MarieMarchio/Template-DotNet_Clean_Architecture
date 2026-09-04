using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

namespace AppTemplate.Worker.UnitTests.Features.Files;

/// <summary>Counts calls and always succeeds with a fixed count, unless told to throw.</summary>
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

/// <summary>Counts calls and always succeeds with a fixed count, unless told to throw.</summary>
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

/// <summary>
/// Never completes on its own: it only ever returns by observing the cancellation token, so it can
/// stand in for "a sweep that is mid-flight when the host is asked to stop" — which for the orphan
/// sweep is the ordinary case rather than a contrived one, since a pass walks the entire store.
/// </summary>
internal sealed class HangingOrphanedContentSweep : IReclaimOrphanedContentUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}
