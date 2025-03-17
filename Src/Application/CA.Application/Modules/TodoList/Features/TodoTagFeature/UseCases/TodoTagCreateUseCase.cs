using CA.Application.Commons.Bases;
using CA.Application.Commons.Interfaces.Services;
using CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;
using CA.Application.Modules.TodoList.Features.TodoTagFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;
using CA.Domain.Modules.TodoList.Entity;
using FluentValidation;

namespace CA.Application.Modules.TodoList.Features.TodoTagFeature.UseCases;

public sealed class TodoTagCreateUseCase(ITodoTagRepository repository, IAuthenticatedUserService authenticatedUser)
    : BaseCreateUseCase<TodoTag, TodoTagCreateRequest, TodoTagDTO>(repository), ITodoTagCreateUseCase
{
    protected override TodoTag MapToEntity(TodoTagCreateRequest request)
    {
        return new TodoTag
        {
            Name = request.TagName,
            UserId = Guid.Parse(authenticatedUser.UserId),
        };
    }
}

class TodoTagCreateValidator : AbstractValidator<ListTodosCreateRequest>
{
    public TodoTagCreateValidator()
    {

    }
}

public class TodoTagCreateRequest
{
    public required string TagName { get; set; }
}