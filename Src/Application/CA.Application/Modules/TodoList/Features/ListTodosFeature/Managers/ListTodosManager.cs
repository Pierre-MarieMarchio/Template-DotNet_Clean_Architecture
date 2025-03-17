using CA.Application.Modules.TodoList.Interfaces.Managers;
using CA.Application.Modules.TodoList.Interfaces.UseCases;

namespace CA.Application.Modules.TodoList.Features.ListTodosFeature.Managers
{
    public class ListTodosManager : IListTodosManager
    {
        public IListTodosGetAllUseCase GetAll { get; }

        public IListTodosGetByIdUseCase GetById { get; }

        public IListTodosCreateUseCase Create { get; }

        public IListTodosUpdateUseCase Update { get; }

        public IListTodosDeleteUseCase Delete { get; }

        public ListTodosManager(IListTodosGetAllUseCase getAll, IListTodosGetByIdUseCase getById, IListTodosCreateUseCase create, IListTodosUpdateUseCase update, IListTodosDeleteUseCase delete)
        {
            GetAll = getAll;
            GetById = getById;
            Create = create;
            Update = update;
            Delete = delete;
        }
    }
}
