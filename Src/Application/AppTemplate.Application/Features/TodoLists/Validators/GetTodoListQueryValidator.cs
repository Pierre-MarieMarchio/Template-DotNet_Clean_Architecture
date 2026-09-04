using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.Validators;

public sealed class GetTodoListQueryValidator : AbstractValidator<GetTodoListQuery>
{
    public GetTodoListQueryValidator() =>
        RuleFor(query => query.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");
}
