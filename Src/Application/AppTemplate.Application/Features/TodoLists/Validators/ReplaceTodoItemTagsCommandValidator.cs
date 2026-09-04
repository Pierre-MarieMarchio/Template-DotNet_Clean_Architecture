using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class ReplaceTodoItemTagsCommandValidator : AbstractValidator<ReplaceTodoItemTagsCommand>
{
    public ReplaceTodoItemTagsCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");

        // Stops at the first failure: the count rule below dereferences the set.
        RuleFor(command => command.Tags)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A tag set is required; send an empty list to clear the item's tags.")
            .Must(tags => tags.Count <= TodoItem.MaxTags)
            .WithMessage($"An item cannot carry more than {TodoItem.MaxTags} tags.");

        RuleForEach(command => command.Tags)
            .NotEmpty().WithMessage("A tag cannot be blank.")
            .Must(tag => tag.Trim().Length <= Tag.MaxLength)
            .WithMessage($"A tag cannot exceed {Tag.MaxLength} characters.");
    }
}
