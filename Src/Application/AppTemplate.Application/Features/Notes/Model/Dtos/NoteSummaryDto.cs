namespace AppTemplate.Application.Features.Notes.Model.Dtos;

public sealed record NoteSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt);
