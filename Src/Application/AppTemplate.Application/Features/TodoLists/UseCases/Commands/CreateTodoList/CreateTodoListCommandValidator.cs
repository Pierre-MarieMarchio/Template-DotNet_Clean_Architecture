using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;

public sealed class CreateTodoListCommandValidator : AbstractValidator<CreateTodoListCommand>
{
    public CreateTodoListCommandValidator() =>
        RuleFor(command => command.Name)
            // Every Must below dereferences the value, and FluentValidation runs the remaining rules
            // for a property even after NotEmpty has failed.
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("A list name is required.")
            // Measured after trimming, like the domain measures it: a 200-character name
            // followed by a space is one the domain accepts, so the validator must too.
            .Must(name => name.Trim().Length <= TodoListName.MaxLength)
            .WithMessage($"A list name cannot exceed {TodoListName.MaxLength} characters.");
}
