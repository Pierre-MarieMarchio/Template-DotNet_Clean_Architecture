using CA.Application.Commons.Interfaces.Services;
using CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;
using CA.Domain.Modules.TodoList.Entity;
using FluentValidation;

namespace CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;

public class ListTodosCreateUseCase(IListTodosRepository listTodosRepository, IAuthenticatedUserService authenticatedUser) : IListTodosCreateUseCase
{
    public async Task<ListTodosDTO> ExecuteAsync(ListTodosCreateRequest request)
    {
        var item = new ListTodos
        {
            Name = request.ListName,
            UserId = Guid.Parse(authenticatedUser.UserId),
        };

        var result = await listTodosRepository.AddAsync(item);
        var res = new ListTodosDTO(result);
        return res;
    }
}

class ListTodosCreateValidator : AbstractValidator<ListTodosCreateRequest>
{
    public ListTodosCreateValidator()
    {



    }
}


public class ListTodosCreateRequest
{
    public required string ListName { get; set; }
}
