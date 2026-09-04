using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
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
/// <para>
/// <b>It deliberately does not check that the item still exists</b>, which is why this route answers
/// <c>200</c> with an empty list where every other route into an item answers <c>404</c>. A reminder
/// is its own aggregate root and outlives the item it is about: removing an item cancels its
/// reminders, it does not delete them, and this is the only route that can show one — a
/// <c>404</c> here would make a cancelled reminder of a removed item unreachable, which is a worse
/// answer than an empty list for an id that was never real. Nothing leaks either way: the owner
/// filter below means a stranger sees an empty list whether the item is theirs, somebody else's or
/// nobody's.
/// </para>
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
                .Select(ReminderDtoMapping.ToDto),
        ];

        // Result<TValue>'s implicit operator does not apply here: TValue is an interface type,
        // and C# never considers a user-defined conversion when the source or target is one.
        return Result.Success(reminders);
    }
}
