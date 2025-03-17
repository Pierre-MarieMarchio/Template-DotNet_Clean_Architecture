using CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;

public class ListTodosGetAllUseCase(IListTodosRepository listTodosRepository) : IListTodosGetAllUseCase
{
    public async Task<List<ListTodosDTO>> ExecuteAsync()
    {
        var result = await listTodosRepository.GetAllAsync();

        var response = (result ?? []).Select(item => new ListTodosDTO(item)).ToList();

        return response;
    }
}
