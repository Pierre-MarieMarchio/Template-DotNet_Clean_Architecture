using CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.TodoItemFeature.UseCases;

public class ListTodosGetByIdUseCase(ITodoItemRepository todoItemRepository) : ITodoItemGetByIdUseCase
{
    public async Task<TodoItemDTO> ExecuteAsync(Guid request)
    {
        var result = await todoItemRepository.GetByIdAsync(request);

        var res = new TodoItemDTO(result);

        return res;
    }
}
