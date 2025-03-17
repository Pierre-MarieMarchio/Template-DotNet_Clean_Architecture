using CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;

public class ListTodosGetByIdUseCase(IListTodosRepository listTodosRepository) : IListTodosGetByIdUseCase
{
    public async Task<ListTodosDTO> ExecuteAsync(Guid request)
    {
        var result = await listTodosRepository.GetByIdAsync(request);

        var res = new ListTodosDTO(result);

        return res;
    }
}
