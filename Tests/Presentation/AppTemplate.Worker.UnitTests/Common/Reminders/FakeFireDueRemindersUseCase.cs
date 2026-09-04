using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

namespace AppTemplate.Worker.UnitTests.Common.Reminders;

/// <summary>Counts calls and always succeeds with a fixed count, unless told to throw.</summary>
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

/// <summary>
/// Never completes on its own: it only ever returns by observing the cancellation token, so it can
/// stand in for "a use case that is mid-flight when the host is asked to stop".
/// </summary>
internal sealed class HangingFireDueRemindersUseCase : IFireDueRemindersUseCase
{
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}
