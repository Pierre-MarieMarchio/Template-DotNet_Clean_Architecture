namespace AppTemplate.Api.Features.TodoLists.Contracts.Responses;

/// <summary>
/// The wire shape of one list inside a page. Carries counts rather than the items themselves, so the
/// size of a page does not grow with the size of the lists on it.
/// </summary>
public sealed record TodoListSummaryResponse(
    Guid Id,
    string Name,
    int ItemCount,
    int CompletedItemCount,
    DateTimeOffset CreatedAt);
