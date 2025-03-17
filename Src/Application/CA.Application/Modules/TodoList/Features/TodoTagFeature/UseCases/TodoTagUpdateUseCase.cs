using CA.Application.Commons.Bases;
using CA.Application.Commons.Interfaces.Services;
using CA.Application.Modules.TodoList.Features.TodoTagFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;
using CA.Domain.Modules.TodoList.Entity;
using FluentValidation;


namespace CA.Application.Modules.TodoList.Features.TodoTagFeature.UseCases;

class TodoTagUpdateUseCase(ITodoTagRepository repository, IAuthenticatedUserService authenticatedUser)
    : BaseUpdateUseCase<TodoTag, TodoTagUpdateRequest, TodoTagDTO>(repository), ITodoTagUpdateUseCase
{

    protected override TodoTag MapToEntity(TodoTagUpdateRequest request)
    {
        return new TodoTag
        {
            Id = request.Id,
            Name = request.TagName,
            TodoItems = request.TodoItemsIds?.Select(todoItemId => new TodoItem { Id = todoItemId }).ToList(),
            UserId = Guid.Parse(authenticatedUser.UserId),
        };
    }

}

class TodoTagUpdateValidator : AbstractValidator<TodoTagUpdateRequest>
{
    public TodoTagUpdateValidator()
    {

    }
}

public class TodoTagUpdateRequest
{
    public required Guid Id { get; set; }
    public required string TagName { get; set; }
    public ICollection<Guid>? TodoItemsIds { get; set; }
}