using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;

public sealed class AddTodoItemCommandValidator : AbstractValidator<AddTodoItemCommand>
{
    public AddTodoItemCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.Title)
            // Every Must below dereferences the value, and FluentValidation runs the remaining rules
            // for a property even after NotEmpty has failed.
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("An item title is required.")
            // Measured after trimming, like the domain measures it.
            .Must(title => title.Trim().Length <= TodoItemTitle.MaxLength)
            .WithMessage($"An item title cannot exceed {TodoItemTitle.MaxLength} characters.");

        RuleFor(command => command.Description)
            .Must(description => description is null || description.Trim().Length <= TodoItem.MaxDescriptionLength)
            .WithMessage($"An item description cannot exceed {TodoItem.MaxDescriptionLength} characters.");

        // Bounds the collection itself, not just each element: the use case adds tags one at a
        // time, and each add is linear in the tags already on the item.
        RuleFor(command => command.Tags)
            .Must(tags => tags is null || tags.Count <= TodoItem.MaxTags)
            .WithMessage($"An item cannot carry more than {TodoItem.MaxTags} tags.");

        RuleForEach(command => command.Tags)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A tag cannot be blank.")
            .Must(tag => tag.Trim().Length <= Tag.MaxLength)
            .WithMessage($"A tag cannot exceed {Tag.MaxLength} characters.");
    }
}
