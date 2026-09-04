using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public interface IDeleteTodoListUseCase : IUseCase<DeleteTodoListCommand, Result>;
