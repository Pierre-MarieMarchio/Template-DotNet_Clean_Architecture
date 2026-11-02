using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;

public sealed class GetTodoItemsQueryValidator : AbstractValidator<GetTodoItemsQuery>
{
    public GetTodoItemsQueryValidator() =>
        RuleFor(query => query.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");
}
