namespace AppTemplate.Application.Features.TodoLists.Dtos;

public sealed record TodoListSummaryDto(
    Guid Id,
    string Name,
    int ItemCount,
    int CompletedItemCount,
    DateTimeOffset CreatedAt);
