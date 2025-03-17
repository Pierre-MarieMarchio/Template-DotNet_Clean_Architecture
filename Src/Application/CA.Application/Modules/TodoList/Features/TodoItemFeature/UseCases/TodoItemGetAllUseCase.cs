using CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.TodoItemFeature.UseCases;

public class ListTodosGetAllUseCase(ITodoItemRepository todoItemRepository) : ITodoItemGetAllUseCase
{
    public async Task<List<TodoItemDTO>> ExecuteAsync()
    {
        var result = await todoItemRepository.GetAllAsync();

        var response = (result ?? []).Select(item => new TodoItemDTO(item)).ToList();

        return response;
    }
}
