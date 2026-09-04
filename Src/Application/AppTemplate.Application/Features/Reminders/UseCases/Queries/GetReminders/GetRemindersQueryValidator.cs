using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

public sealed class GetRemindersQueryValidator : AbstractValidator<GetRemindersQuery>
{
    public GetRemindersQueryValidator()
    {
        RuleFor(query => query.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(query => query.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");
    }
}
