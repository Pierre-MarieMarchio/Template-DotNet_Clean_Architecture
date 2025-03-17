using CA.Application.Modules.TodoList.Interfaces.Managers;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.TodoItemFeature.Managers;

public class TodoItemManager : ITodoItemManager
{
    public ITodoItemGetAllUseCase GetAll { get; }

    public ITodoItemGetByIdUseCase GetById { get; }

    public ITodoItemCreateUseCase Create { get; }

    public ITodoItemUpdateUseCase Update { get; }

    public ITodoItemDeleteUseCase Delete { get; }

    public TodoItemManager(ITodoItemGetAllUseCase getAll, ITodoItemGetByIdUseCase getById, ITodoItemCreateUseCase create, ITodoItemUpdateUseCase update, ITodoItemDeleteUseCase delete)
    {
        GetAll = getAll;
        GetById = getById;
        Create = create;
        Update = update;
        Delete = delete;
    }
}
