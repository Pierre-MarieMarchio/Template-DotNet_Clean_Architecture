using CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.TodoItemFeature.UseCases;

class ListTodosDeleteUseCase(ITodoItemRepository todoItemRepository) : ITodoItemDeleteUseCase
{
    public async Task<TodoItemDTO> ExecuteAsync(Guid request)
    {
        var item = await todoItemRepository.GetByIdAsync(request);
        var deletedItem = await todoItemRepository.DeleteAsync(item);
        var res = new TodoItemDTO(deletedItem);

        return res;
    }
}
