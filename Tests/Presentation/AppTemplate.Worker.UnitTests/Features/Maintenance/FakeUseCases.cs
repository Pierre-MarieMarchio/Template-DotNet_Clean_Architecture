using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;

namespace AppTemplate.Worker.UnitTests.Features.Maintenance;

/// <summary>Counts calls and always succeeds with a fixed count, unless told to throw.</summary>
internal sealed class FakeIdempotencyPurge : IPurgeExpiredIdempotencyKeysUseCase
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeIdempotencyPurge(Exception? failure = null, int count = 3)
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
internal sealed class FakeRefreshTokenPurge : IPurgeExpiredRefreshTokensUseCase
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeRefreshTokenPurge(Exception? failure = null, int count = 5)
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
/// Never completes on its own: it only ever returns by observing the cancellation token, so it
/// can stand in for "a use case that is mid-flight when the host is asked to stop".
/// </summary>
internal sealed class HangingUseCase : IPurgeExpiredIdempotencyKeysUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}
