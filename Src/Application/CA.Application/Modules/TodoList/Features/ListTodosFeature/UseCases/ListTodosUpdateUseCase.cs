using CA.Application.Commons.Interfaces.Services;
using CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;
using CA.Domain.Modules.TodoList.Entity;
using FluentValidation;


namespace CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;

public class ListTodosUpdateUseCase(IListTodosRepository listTodosRepository, IAuthenticatedUserService authenticatedUser) : IListTodosUpdateUseCase
{
    public async Task<ListTodosDTO> ExecuteAsync(ListTodosUpdateRequest request)
    {
        var item = new ListTodos
        {
            Id = request.Id,
            Name = request.Name,
            UserId = Guid.Parse(authenticatedUser.UserId),
        };

        var result = await listTodosRepository.UpdateAsync(item);

        var res = new ListTodosDTO(result);

        return res;
    }
}

class ListTodosUpdate : AbstractValidator<ListTodosUpdateRequest>
{
    public ListTodosUpdate()
    {

    }
}

public class ListTodosUpdateRequest
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }

}
