namespace AppTemplate.Application.Features.Notes.Model.Dtos;

public sealed record NoteDto(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt);
