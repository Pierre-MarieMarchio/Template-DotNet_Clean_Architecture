using AppTemplate.Application.Common.Tagging;
using AppTemplate.Domain.Features.TodoLists.Entities;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;

public sealed class ReplaceTodoItemTagsCommandValidator : AbstractValidator<ReplaceTodoItemTagsCommand>
{
    public ReplaceTodoItemTagsCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");

        RuleFor(command => command.Tags)
            .IsATagSet(TodoItem.MaxTags, "to-do item");

        RuleForEach(command => command.Tags)
            .IsATag();
    }
}
