using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Helpers;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;

public sealed record AddTagToTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Tag,
    VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<TodoItemDto>>>;

public sealed class AddTagToTodoItemCommandHandler(
    ITodoListService lists,
    IUnitOfWork unitOfWork,
    ICacheStore cache) : ICommandHandler<AddTagToTodoItemCommand, Result<Versioned<TodoItemDto>>>
{
    public async ValueTask<Result<Versioned<TodoItemDto>>> Handle(
        AddTagToTodoItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await lists.LoadOwnedAsync(command.TodoListId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<TodoItemDto>>();
        }

        var todoList = access.Value;

        var found = todoList.RequireItem(command.TodoItemId);

        if (found.IsFailure)
        {
            return found.To<Versioned<TodoItemDto>>();
        }

        var addition = DomainGuard.Try(() => todoList.AddTagToItem(command.TodoItemId, command.Tag));

        if (addition.IsFailure)
        {
            return addition.To<Versioned<TodoItemDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveAsync(
            UsedTagsCache.KeyFor(UsedTagsCache.TodoItemScope, todoList.OwnerId),
            cancellationToken);

        return TodoListDtoMapping.Item(todoList, command.TodoItemId);
    }
}
