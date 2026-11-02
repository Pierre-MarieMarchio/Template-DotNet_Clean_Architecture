using AppTemplate.Application.Common.Tagging;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;

public sealed class RemoveTagFromTodoItemCommandValidator : AbstractValidator<RemoveTagFromTodoItemCommand>
{
    public RemoveTagFromTodoItemCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");

        RuleFor(command => command.Tag)
            .IsATag();
    }
}
