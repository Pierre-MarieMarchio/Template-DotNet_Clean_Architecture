using CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;

class ListTodosDeleteUseCase(IListTodosRepository listTodosRepository) : IListTodosDeleteUseCase
{
    public async Task<ListTodosDTO> ExecuteAsync(Guid request)
    {
        var item = await listTodosRepository.GetByIdAsync(request);
        var deletedItem = await listTodosRepository.DeleteAsync(item);
        var res = new ListTodosDTO(deletedItem);

        return res;
    }
}
