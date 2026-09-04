using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;

public interface IReplaceTodoItemTagsUseCase
    : IUseCase<ReplaceTodoItemTagsCommand, Result<Versioned<TodoItemDto>>>;
