using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class AddTodoItemCommandValidator : AbstractValidator<AddTodoItemCommand>
{
    public AddTodoItemCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.Title)
            .NotEmpty().WithMessage("An item title is required.")
            .MaximumLength(TodoItemTitle.MaxLength)
            .WithMessage($"An item title cannot exceed {TodoItemTitle.MaxLength} characters.");

        RuleFor(command => command.Description)
            .MaximumLength(TodoItem.MaxDescriptionLength)
            .WithMessage($"An item description cannot exceed {TodoItem.MaxDescriptionLength} characters.");

        // Bounds the collection itself, not just each element: the use case adds tags one at a
        // time, and each add is linear in the tags already on the item.
        RuleFor(command => command.Tags)
            .Must(tags => tags is null || tags.Count <= TodoItem.MaxTags)
            .WithMessage($"An item cannot carry more than {TodoItem.MaxTags} tags.");

        RuleForEach(command => command.Tags)
            .NotEmpty().WithMessage("A tag cannot be blank.")
            .MaximumLength(Tag.MaxLength)
            .WithMessage($"A tag cannot exceed {Tag.MaxLength} characters.");
    }
}
