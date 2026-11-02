//#if (localAuth)
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;
//#endif
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using Mediator;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Jobs;

internal sealed class FakeIdempotencyPurge : ICommandHandler<PurgeExpiredIdempotencyKeysCommand, Result<int>>
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeIdempotencyPurge(Exception? failure = null, int count = 3)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public ValueTask<Result<int>> Handle(PurgeExpiredIdempotencyKeysCommand command, CancellationToken cancellationToken)
    {
        CallCount++;

        if (_failure is not null)
        {
            return ValueTask.FromException<Result<int>>(_failure);
        }

        return ValueTask.FromResult(Result.Success(_count));
    }
}

//#if (localAuth)
internal sealed class FakeRefreshTokenPurge : ICommandHandler<PurgeExpiredRefreshTokensCommand, Result<int>>
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeRefreshTokenPurge(Exception? failure = null, int count = 5)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public ValueTask<Result<int>> Handle(PurgeExpiredRefreshTokensCommand command, CancellationToken cancellationToken)
    {
        CallCount++;

        if (_failure is not null)
        {
            return ValueTask.FromException<Result<int>>(_failure);
        }

        return ValueTask.FromResult(Result.Success(_count));
    }
}

//#endif
internal sealed class HangingIdempotencyPurge : ICommandHandler<PurgeExpiredIdempotencyKeysCommand, Result<int>>
{
    private volatile bool _hasEntered;

    public bool HasEntered => _hasEntered;

    public async ValueTask<Result<int>> Handle(PurgeExpiredIdempotencyKeysCommand command, CancellationToken cancellationToken)
    {
        _hasEntered = true;

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}

//#if (localAuth)
internal sealed class FakeSecurityEventPurge : ICommandHandler<PurgeExpiredSecurityEventsCommand, Result<int>>
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeSecurityEventPurge(Exception? failure = null, int count = 7)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public ValueTask<Result<int>> Handle(PurgeExpiredSecurityEventsCommand command, CancellationToken cancellationToken)
    {
        CallCount++;

        if (_failure is not null)
        {
            return ValueTask.FromException<Result<int>>(_failure);
        }

        return ValueTask.FromResult(Result.Success(_count));
    }
}
//#endif
