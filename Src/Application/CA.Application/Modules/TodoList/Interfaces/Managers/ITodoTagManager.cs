using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Interfaces.Managers;

public interface ITodoTagManager
{
    ITodoTagGetAllUseCase GetAll { get; }
    ITodoTagGetByIdUseCase GetById { get; }
    ITodoTagCreateUseCase Create { get; }
    ITodoTagUpdateUseCase Update { get; }
    ITodoTagDeleteUseCase Delete { get; }
}
