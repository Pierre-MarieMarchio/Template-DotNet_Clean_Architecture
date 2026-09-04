using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class UpdateTodoItemCommandValidator : AbstractValidator<UpdateTodoItemCommand>
{
    public UpdateTodoItemCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");

        RuleFor(command => command.Title)
            .NotEmpty().WithMessage("An item title is required.")
            // Measured after trimming, like the domain measures it.
            .Must(title => title.Trim().Length <= TodoItemTitle.MaxLength)
            .WithMessage($"An item title cannot exceed {TodoItemTitle.MaxLength} characters.");

        RuleFor(command => command.Description)
            .Must(description => description is null || description.Trim().Length <= TodoItem.MaxDescriptionLength)
            .WithMessage($"An item description cannot exceed {TodoItem.MaxDescriptionLength} characters.");
    }
}
