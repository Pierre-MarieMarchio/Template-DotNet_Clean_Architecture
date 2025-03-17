using CA.Application.Commons.Interfaces.UseCase;
using CA.Application.Modules.TodoList.Features.TodoItemFeature.DTOs;
using CA.Application.Modules.TodoList.Features.TodoItemFeature.UseCases;

namespace CA.Application.Modules.TodoList.Interfaces.UseCases;

public interface ITodoItemGetAllUseCase : IBaseUseCase<List<TodoItemDTO>>
{
}

public interface ITodoItemGetByIdUseCase : IBaseUseCase<Guid, TodoItemDTO>
{
}

public interface ITodoItemCreateUseCase : IBaseUseCase<TodoItemCreateRequest, TodoItemDTO>
{
}
public interface ITodoItemUpdateUseCase : IBaseUseCase<TodoItemUpdateRequest, TodoItemDTO>
{
}

public interface ITodoItemDeleteUseCase : IBaseUseCase<Guid, TodoItemDTO>
{
}