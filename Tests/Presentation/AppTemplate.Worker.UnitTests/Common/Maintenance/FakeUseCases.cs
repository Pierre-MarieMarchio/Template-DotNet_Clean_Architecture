using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands;

namespace AppTemplate.Worker.UnitTests.Common.Maintenance;

/// <summary>Counts calls and always succeeds with a fixed count, unless told to throw.</summary>
internal sealed class FakeIdempotencyPurge : IPurgeExpiredIdempotencyKeysUseCase
{
    private readonly Exception? _failure;

    public FakeIdempotencyPurge(Exception? failure = null) => _failure = failure;

    public int CallCount { get; private set; }

    public Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_failure is not null)
        {
            return Task.FromException<Result<int>>(_failure);
        }

        return Task.FromResult(Result.Success(3));
    }
}

/// <summary>Counts calls and always succeeds with a fixed count, unless told to throw.</summary>
internal sealed class FakeRefreshTokenPurge : IPurgeExpiredRefreshTokensUseCase
{
    private readonly Exception? _failure;

    public FakeRefreshTokenPurge(Exception? failure = null) => _failure = failure;

    public int CallCount { get; private set; }

    public Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_failure is not null)
        {
            return Task.FromException<Result<int>>(_failure);
        }

        return Task.FromResult(Result.Success(5));
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
