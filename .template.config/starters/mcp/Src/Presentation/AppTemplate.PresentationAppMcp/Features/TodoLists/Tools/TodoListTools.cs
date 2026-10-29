using System.ComponentModel;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using AppTemplate.PresentationApp.Common.Security;
using AppTemplate.PresentationApp.Common.Tools;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AppTemplate.PresentationApp.Features.TodoLists.Tools;

[McpServerToolType]
internal sealed class TodoListTools(
    IGetTodoListsUseCase getTodoLists,
    IGetTodoListUseCase getTodoList,
    ICreateTodoListUseCase createTodoList,
    IAddTodoItemUseCase addTodoItem)
{
    [McpServerTool(Name = "list_todo_lists", Title = "List to-do lists", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Lists the caller's own to-do lists, a page at a time, without their items.")]
    public async Task<CallToolResult> ListTodoListsAsync(
        [Description("The page to return, from 1. The first page when omitted.")] int? page = null,
        [Description("How many lists a page holds. The API's default when omitted.")] int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await getTodoLists.ExecuteAsync(GetTodoListsQuery.Offset(page, pageSize), cancellationToken),
            lists => lists);

    [McpServerTool(Name = "get_todo_list", Title = "Get a to-do list", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Gets one of the caller's to-do lists with its items. A list that is not the caller's answers as one that does not exist.")]
    public async Task<CallToolResult> GetTodoListAsync(
        [Description("The list's id, as list_todo_lists gives it.")] Guid todoListId,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await getTodoList.ExecuteAsync(new GetTodoListQuery(todoListId), cancellationToken),
            list => list.Value);

    [McpServerTool(Name = "create_todo_list", Title = "Create a to-do list", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Write)]
    [Description("Creates an empty to-do list owned by the caller and returns it.")]
    public async Task<CallToolResult> CreateTodoListAsync(
        [Description("The list's name.")] string name,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await createTodoList.ExecuteAsync(new CreateTodoListCommand(name), cancellationToken),
            list => list.Value);

    [McpServerTool(Name = "add_todo_item", Title = "Add a to-do item", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Write)]
    [Description("Adds an item to one of the caller's to-do lists and returns it.")]
    public async Task<CallToolResult> AddTodoItemAsync(
        [Description("The list's id, as list_todo_lists gives it.")] Guid todoListId,
        [Description("The item's title.")] string title,
        [Description("A longer description, or none.")] string? description = null,
        [Description("Tags to file the item under, or none.")] IReadOnlyList<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await addTodoItem.ExecuteAsync(new AddTodoItemCommand(todoListId, title, description, tags), cancellationToken),
            item => item.Value);
}
