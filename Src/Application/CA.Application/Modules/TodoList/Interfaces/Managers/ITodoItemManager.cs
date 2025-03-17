using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Interfaces.Managers;

public interface ITodoItemManager
{
    ITodoItemGetAllUseCase GetAll { get; }
    ITodoItemGetByIdUseCase GetById { get; }
    ITodoItemCreateUseCase Create { get; }
    ITodoItemUpdateUseCase Update { get; }
    ITodoItemDeleteUseCase Delete { get; }

}
