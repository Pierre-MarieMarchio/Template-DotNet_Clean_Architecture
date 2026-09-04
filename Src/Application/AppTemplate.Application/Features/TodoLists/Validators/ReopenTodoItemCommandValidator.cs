using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class ReopenTodoItemCommandValidator : AbstractValidator<ReopenTodoItemCommand>
{
    public ReopenTodoItemCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");
    }
}
