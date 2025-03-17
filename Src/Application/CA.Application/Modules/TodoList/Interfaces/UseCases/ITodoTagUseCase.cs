using CA.Application.Commons.Interfaces.UseCase;
using CA.Application.Modules.TodoList.Features.TodoTagFeature.DTOs;
using CA.Application.Modules.TodoList.Features.TodoTagFeature.UseCases;

namespace CA.Application.Modules.TodoList.Interfaces.UseCases;

public interface ITodoTagGetAllUseCase : IBaseUseCase<List<TodoTagDTO>>
{
}

public interface ITodoTagGetByIdUseCase : IBaseUseCase<Guid, TodoTagDTO>
{
}

public interface ITodoTagCreateUseCase : IBaseUseCase<TodoTagCreateRequest, TodoTagDTO>
{
}
public interface ITodoTagUpdateUseCase : IBaseUseCase<TodoTagUpdateRequest, TodoTagDTO>
{
}

public interface ITodoTagDeleteUseCase : IBaseUseCase<Guid, TodoTagDTO>
{
}