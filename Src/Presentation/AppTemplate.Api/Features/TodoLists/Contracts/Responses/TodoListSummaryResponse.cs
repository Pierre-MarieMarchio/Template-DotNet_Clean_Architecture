namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>The wire shape of one list inside a page.</summary>
public sealed record TodoListSummaryResponse(
    Guid Id,
    string Name,
    int ItemCount,
    int CompletedItemCount,
    DateTimeOffset CreatedAt);
