using CA.Application.Commons.Interfaces.UseCase;
using CA.Application.Modules.TodoList.Features.ListTodosFeature.DTOs;
using CA.Application.Modules.TodoList.Features.ListTodosFeature.UseCases;

namespace CA.Application.Modules.TodoList.Interfaces.UseCases;

public interface IListTodosGetAllUseCase : IBaseUseCase<List<ListTodosDTO>>
{
}

public interface IListTodosGetByIdUseCase : IBaseUseCase<Guid, ListTodosDTO>
{
}

public interface IListTodosCreateUseCase : IBaseUseCase<ListTodosCreateRequest, ListTodosDTO>
{
}
public interface IListTodosUpdateUseCase : IBaseUseCase<ListTodosUpdateRequest, ListTodosDTO>
{
}

public interface IListTodosDeleteUseCase : IBaseUseCase<Guid, ListTodosDTO>
{
}