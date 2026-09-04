using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class GetTodoItemQueryValidator : AbstractValidator<GetTodoItemQuery>
{
    public GetTodoItemQueryValidator()
    {
        RuleFor(query => query.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(query => query.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");
    }
}
