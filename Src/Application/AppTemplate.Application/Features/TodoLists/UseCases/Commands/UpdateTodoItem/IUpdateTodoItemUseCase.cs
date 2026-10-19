using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;

public interface IUpdateTodoItemUseCase : IUseCase<UpdateTodoItemCommand, Result<Versioned<TodoItemDto>>>;
