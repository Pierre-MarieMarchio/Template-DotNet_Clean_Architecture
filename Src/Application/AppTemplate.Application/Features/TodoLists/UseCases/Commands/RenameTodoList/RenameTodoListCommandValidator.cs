using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;

public sealed class RenameTodoListCommandValidator : AbstractValidator<RenameTodoListCommand>
{
    public RenameTodoListCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("A list name is required.")
            // Measured after trimming, like the domain measures it.
            .Must(name => name.Trim().Length <= TodoListName.MaxLength)
            .WithMessage($"A list name cannot exceed {TodoListName.MaxLength} characters.");
    }
}
