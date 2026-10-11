using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

/// <summary>Reads through <see cref="IReminderQueries"/>, which filters to the caller's own
/// reminders in the query itself and projects straight to DTOs; a read, so it writes nothing and
/// needs no transaction, token or retry policy.</summary>
/// <remarks>Deliberately does not check that the item still exists, so this route answers <c>200</c>
/// with an empty list where every other route into an item answers <c>404</c>: removing an item
/// cancels its reminders, it does not delete them, and a <c>404</c> would make a cancelled reminder
/// unreachable. Nothing leaks either way — the owner filter means a stranger sees an empty list
/// whether the item is theirs, somebody else's or nobody's.</remarks>
public sealed class GetRemindersUseCase(
    IReminderQueries queries,
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

        var reminders = await queries.GetForTodoItemAsync(
            userId.Value,
            query.TodoListId,
            query.TodoItemId,
            cancellationToken);

        // Result<TValue>'s implicit operator does not apply here: TValue is an interface type,
        // and C# never considers a user-defined conversion when the source or target is one.
        return Result.Success(reminders);
    }
}
