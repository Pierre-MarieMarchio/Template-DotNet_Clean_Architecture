using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;

public interface IRenameTodoListUseCase : IUseCase<RenameTodoListCommand, Result<Versioned<TodoListDetailDto>>>;
