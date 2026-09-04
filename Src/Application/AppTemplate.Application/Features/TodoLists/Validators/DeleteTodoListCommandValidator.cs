using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class DeleteTodoListCommandValidator : AbstractValidator<DeleteTodoListCommand>
{
    public DeleteTodoListCommandValidator() =>
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");
}
