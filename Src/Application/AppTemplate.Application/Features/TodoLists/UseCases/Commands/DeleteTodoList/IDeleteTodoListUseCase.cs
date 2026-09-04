using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public interface IDeleteTodoListUseCase : IUseCase<DeleteTodoListCommand, Result>;
