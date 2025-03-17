using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Interfaces.Managers;

public interface IListTodosManager
{
    IListTodosGetAllUseCase GetAll { get; }
    IListTodosGetByIdUseCase GetById { get; }
    IListTodosCreateUseCase Create { get; }
    IListTodosUpdateUseCase Update { get; }
    IListTodosDeleteUseCase Delete { get; }

}
