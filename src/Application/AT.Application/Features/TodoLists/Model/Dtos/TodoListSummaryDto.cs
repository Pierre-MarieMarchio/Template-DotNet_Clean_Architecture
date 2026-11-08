namespace AppTemplate.Application.Features.TodoLists.Model.Dtos;

public sealed record TodoListSummaryDto(
    Guid Id,
    string Name,
    int ItemCount,
    int CompletedItemCount,
    DateTimeOffset CreatedAt);
