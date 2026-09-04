using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Dtos;

namespace AppTemplate.Api.Features.TodoLists.Mapping;

/// <summary>
/// Projects the feature's application DTOs onto its wire contracts, by hand.
/// </summary>
/// <remarks>
/// By hand, for the reason <c>docs/adr/0011-persistence-models-separate-from-the-domain.md</c> gives
/// for the other boundary: positional records plus <c>TreatWarningsAsErrors</c> make a member added
/// on either side fail the build here, where a convention-based mapper would have turned the
/// forgotten field into a naming rule nobody reads.
/// </remarks>
internal static class TodoListMapping
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
        Result<PagedResult<TodoListSummaryDto>> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Value throws on a failure, so the guard is a statement of its own rather than a subpattern.
        if (result.IsFailure)
        {
            return result.To<PagedResponse<TodoListSummaryResponse>>();
        }

        return PagedResponse.From(result.Value, ToResponse);
    }

    public static Result<Versioned<TodoListResponse>> ToListResponse(Result<Versioned<TodoListDetailDto>> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<Versioned<TodoListResponse>>();
        }

        return new Versioned<TodoListResponse>(ToResponse(result.Value.Value), result.Value.Version);
    }

    public static Result<Versioned<TodoItemResponse>> ToItemResponse(Result<Versioned<TodoItemDto>> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<Versioned<TodoItemResponse>>();
        }

        return new Versioned<TodoItemResponse>(ToResponse(result.Value.Value), result.Value.Version);
    }

    public static Result<Versioned<TodoItemsResponse>> ToItemsResponse(
        Result<Versioned<IReadOnlyList<TodoItemDto>>> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<Versioned<TodoItemsResponse>>();
        }

        return new Versioned<TodoItemsResponse>(ToResponse(result.Value.Value), result.Value.Version);
    }
}
