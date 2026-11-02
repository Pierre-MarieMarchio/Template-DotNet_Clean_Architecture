//#if (localAuth)
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;
//#endif
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Jobs;

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

//#if (localAuth)
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

//#endif
internal sealed class HangingUseCase : IPurgeExpiredIdempotencyKeysUseCase
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

//#if (localAuth)
internal sealed class FakeSecurityEventPurge : IPurgeExpiredSecurityEventsUseCase
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeSecurityEventPurge(Exception? failure = null, int count = 7)
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
//#endif
