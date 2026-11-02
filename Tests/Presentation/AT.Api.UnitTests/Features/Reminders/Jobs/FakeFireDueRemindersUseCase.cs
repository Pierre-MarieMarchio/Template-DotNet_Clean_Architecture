using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Jobs;

internal sealed class FakeFireDueRemindersUseCase : IFireDueRemindersUseCase
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeFireDueRemindersUseCase(Exception? failure = null, int count = 3)
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

internal sealed class HangingFireDueRemindersUseCase : IFireDueRemindersUseCase
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
