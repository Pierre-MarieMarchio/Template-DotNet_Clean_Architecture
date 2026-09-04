using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Api.Features.TodoLists.Mapping;

/// <summary>
/// Projects the feature's application DTOs onto its wire contracts, by hand.
/// </summary>
/// <remarks>
/// By hand, for the reason this repository gives
/// for the other boundary: positional records plus <c>TreatWarningsAsErrors</c> make a member added
/// on either side fail the build here, where a convention-based mapper would have turned the
/// forgotten field into a naming rule nobody reads.
/// <para>
/// Every projection here happens to be field for field, which makes the boundary look like pure
/// ceremony; it is the common case, not the reason the boundary exists. Two places in this
/// repository show what it buys:
/// <see cref="AppTemplate.Api.Features.Reminders.Mapping.ReminderResponseMapping"/> answers with a
/// string status so that no client ever depends on the declaration order of a domain enum, and
/// <see cref="AppTemplate.Api.Features.Auth.Contracts.Responses.RegisterResponse"/> withholds the
/// user id its application outcome carries, because nothing downstream of sign-up addresses the
/// account by id. Neither choice survives a contract that is the application type.
/// </para>
/// </remarks>
internal static class TodoListResponseMapping
{
    public static TodoItemResponse ToResponse(TodoItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new TodoItemResponse(
            item.Id,
            item.Title,
            item.Description,
            item.IsCompleted,
            item.CompletedAt,
            item.Tags);
    }

    public static TodoItemsResponse ToResponse(IReadOnlyList<TodoItemDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new TodoItemsResponse([.. items.Select(ToResponse)]);
    }

    public static TodoListResponse ToResponse(TodoListDetailDto list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return new TodoListResponse(
            list.Id,
            list.Name,
            list.CreatedAt,
            list.LastModifiedAt,
            [.. list.Items.Select(ToResponse)]);
    }

    public static TodoListSummaryResponse ToResponse(TodoListSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new TodoListSummaryResponse(
            summary.Id,
            summary.Name,
            summary.ItemCount,
            summary.CompletedItemCount,
            summary.CreatedAt);
    }

    public static Result<PagedResponse<TodoListSummaryResponse>> ToPageResponse(
        Result<PagedResult<TodoListSummaryDto>> result) =>
        result.Map(value => PagedResponse.From(value, ToResponse));

    public static Result<Versioned<TodoListResponse>> ToListResponse(Result<Versioned<TodoListDetailDto>> result) =>
        result.Map(value => new Versioned<TodoListResponse>(ToResponse(value.Value), value.Version));

    public static Result<Versioned<TodoItemResponse>> ToItemResponse(Result<Versioned<TodoItemDto>> result) =>
        result.Map(value => new Versioned<TodoItemResponse>(ToResponse(value.Value), value.Version));

    public static Result<Versioned<TodoItemsResponse>> ToItemsResponse(
        Result<Versioned<IReadOnlyList<TodoItemDto>>> result) =>
        result.Map(value => new Versioned<TodoItemsResponse>(ToResponse(value.Value), value.Version));
}
