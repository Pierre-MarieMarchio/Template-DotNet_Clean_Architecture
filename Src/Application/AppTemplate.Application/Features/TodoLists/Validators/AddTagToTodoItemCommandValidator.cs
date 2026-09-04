using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class AddTagToTodoItemCommandValidator : AbstractValidator<AddTagToTodoItemCommand>
{
    public AddTagToTodoItemCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");

        RuleFor(command => command.Tag)
            .NotEmpty().WithMessage("A tag cannot be blank.")
            // Measured after trimming, like the domain measures it.
            .Must(tag => tag.Trim().Length <= Tag.MaxLength)
            .WithMessage($"A tag cannot exceed {Tag.MaxLength} characters.");
    }
}
