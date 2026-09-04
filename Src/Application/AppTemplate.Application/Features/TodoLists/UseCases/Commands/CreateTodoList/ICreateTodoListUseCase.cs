using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;

public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;
