using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public interface IDeleteTodoListUseCase : IUseCase<DeleteTodoListCommand, Result>;
