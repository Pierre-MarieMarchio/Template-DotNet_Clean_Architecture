using CA.Application.Commons.Bases;
using CA.Application.Modules.TodoList.Features.TodoTagFeature.DTOs;
using CA.Application.Modules.TodoList.Interfaces.Repositories;
using CA.Application.Modules.TodoList.Interfaces.UseCases;
using CA.Domain.Modules.TodoList.Entity;

namespace CA.Application.Modules.TodoList.Features.TodoTagFeature.UseCases;

public class TodoTagGetAllUseCase(ITodoTagRepository repository) : BaseGetAllUseCase<TodoTag, TodoTagDTO>(repository), ITodoTagGetAllUseCase
{

}
