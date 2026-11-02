using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using Mediator;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Jobs;

internal sealed class FakeFireDueRemindersUseCase : ICommandHandler<FireDueRemindersCommand, Result<int>>
{
    private readonly Exception? _failure;
    private readonly int _count;

    public FakeFireDueRemindersUseCase(Exception? failure = null, int count = 3)
    {
        _failure = failure;
        _count = count;
    }

    public int CallCount { get; private set; }

    public ValueTask<Result<int>> Handle(FireDueRemindersCommand command, CancellationToken cancellationToken)
    {
        CallCount++;

        if (_failure is not null)
        {
            return ValueTask.FromException<Result<int>>(_failure);
        }

        return ValueTask.FromResult(Result.Success(_count));
    }
}

internal sealed class HangingFireDueRemindersUseCase : ICommandHandler<FireDueRemindersCommand, Result<int>>
{
    private volatile bool _hasEntered;

    public bool HasEntered => _hasEntered;

    public async ValueTask<Result<int>> Handle(FireDueRemindersCommand command, CancellationToken cancellationToken)
    {
        _hasEntered = true;

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new UnreachableException();
    }

    private sealed class UnreachableException : Exception;
}
