namespace AppTemplate.Application.Features.Notes.Model.Dtos;

/// <summary>A note inside a page: no body, so a page does not grow with the length of the notes on it.</summary>
public sealed record NoteSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt);
