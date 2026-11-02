using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public sealed class DeleteTodoListCommandValidator : AbstractValidator<DeleteTodoListCommand>
{
    public DeleteTodoListCommandValidator() =>
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");
}
