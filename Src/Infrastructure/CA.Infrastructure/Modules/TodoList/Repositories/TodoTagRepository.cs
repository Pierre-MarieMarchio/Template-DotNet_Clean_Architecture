using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Domain.Modules.TodoList.Entity;
using CA.Infrastructure.Commons.Bases;
using CA.Infrastructure.Commons.Contexts;

namespace CA.Infrastructure.Modules.TodoList.Repositories;

public sealed class TodoTagRepository(AppDbContext context) : BaseRepository<TodoTag>(context), ITodoTagRepository
{
}
