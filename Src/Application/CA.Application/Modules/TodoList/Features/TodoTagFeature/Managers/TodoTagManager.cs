using CA.Application.Modules.TodoList.Interfaces.Managers;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.TodoTagFeature.Managers;

public class TodoTagManager : ITodoTagManager
{
    public ITodoTagGetAllUseCase GetAll { get; }

    public ITodoTagGetByIdUseCase GetById { get; }

    public ITodoTagCreateUseCase Create { get; }

    public ITodoTagUpdateUseCase Update { get; }

    public ITodoTagDeleteUseCase Delete { get; }

    public TodoTagManager(ITodoTagGetAllUseCase getAll, ITodoTagGetByIdUseCase getById, ITodoTagCreateUseCase create, ITodoTagUpdateUseCase update, ITodoTagDeleteUseCase delete)
    {
        GetAll = getAll;
        GetById = getById;
        Create = create;
        Update = update;
        Delete = delete;
    }
}
