using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;

public interface ICompleteTodoItemUseCase : IUseCase<CompleteTodoItemCommand, Result<Versioned<TodoItemDto>>>;
