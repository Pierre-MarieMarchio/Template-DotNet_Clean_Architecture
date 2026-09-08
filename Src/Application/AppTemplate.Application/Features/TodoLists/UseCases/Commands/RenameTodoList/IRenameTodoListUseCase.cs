using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;

public interface IRenameTodoListUseCase : IUseCase<RenameTodoListCommand, Result<Versioned<TodoListDetailDto>>>;
