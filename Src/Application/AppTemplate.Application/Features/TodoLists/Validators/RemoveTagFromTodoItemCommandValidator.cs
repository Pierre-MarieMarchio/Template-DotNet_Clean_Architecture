using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class RemoveTagFromTodoItemCommandValidator : AbstractValidator<RemoveTagFromTodoItemCommand>
{
    public RemoveTagFromTodoItemCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");

        RuleFor(command => command.Tag)
            .NotEmpty().WithMessage("A tag cannot be blank.")
            .Must(tag => tag.Trim().Length <= Tag.MaxLength)
            .WithMessage($"A tag cannot exceed {Tag.MaxLength} characters.");
    }
}
