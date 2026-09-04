using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Application.Features.Reminders.Mapping;
using AppTemplate.Domain.Features.Reminders.Repositories;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

/// <summary>
/// <see cref="IReminderRepository.GetForTodoItemAsync"/> is not itself scoped to an owner — it
/// answers "every reminder on this item," the way a consumer cancelling on completion needs it
/// to — so filtering to the caller's own is this use case's job, not the repository's.
/// </summary>
public sealed class GetRemindersUseCase(
    IReminderRepository repository,
    ICurrentUser currentUser,
    IValidator<GetRemindersQuery> validator) : IGetRemindersUseCase
{
    public async Task<Result<IReadOnlyList<ReminderDto>>> ExecuteAsync(
        GetRemindersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<IReadOnlyList<ReminderDto>>();
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<IReadOnlyList<ReminderDto>>();
        }

        var candidates = await repository.GetForTodoItemAsync(query.TodoItemId, cancellationToken);

        IReadOnlyList<ReminderDto> reminders =
        [
            .. candidates
                .Where(reminder => reminder.OwnerId == userId.Value)
                .Where(reminder => reminder.TodoListId == query.TodoListId)
                .OrderBy(reminder => reminder.DueAt)
                .Select(ReminderProjection.ToDto),
        ];

        // Result<TValue>'s implicit operator does not apply here: TValue is an interface type,
        // and C# never considers a user-defined conversion when the source or target is one.
        return Result.Success(reminders);
    }
}
